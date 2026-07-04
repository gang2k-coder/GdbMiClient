using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace GdbMiBridge.Mcp;

/// <summary>
/// Single-threaded debug session. Background ReadLoop reads GDB stdout and
/// feeds lines to GdbMiClient.ProcessLine. The consumer loop serializes commands.
/// *stopped events are intercepted for auto-capture + go/break decisions.
/// </summary>
public class GdbSession : IDisposable
{
    private readonly ILogger<GdbSession> _logger;
    private readonly Channel<SessionOperation> _channel =
        Channel.CreateUnbounded<SessionOperation>(new UnboundedChannelOptions
        { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;
    private readonly BreakpointManager _bpManager = new();
    private readonly CapturesManager _captures = new();

    private GdbMi.LocalTransport? _transport;
    private GdbMi.GdbMiClient? _client;
    private GdbMi.GdbCommandFactory? _cmd;
    private TaskCompletionSource<string>? _waitingForStop;
    private TaskCompletionSource<SessionInfo>? _waitingForFirstStop; // Create completion
    private CancellationTokenSource? _goTimeoutCts;
    private Task? _readLoopTask;
    private CancellationTokenSource? _readLoopCts;
    private int _currentThread = 1;

    public GdbMi.DebuggerState State => _client?.State ?? GdbMi.DebuggerState.NotConnected;
    public BreakpointManager Breakpoints => _bpManager;
    public CapturesManager Captures => _captures;

    public GdbSession(ILogger<GdbSession> logger)
    {
        _logger = logger;
        _consumerTask = ConsumerLoopAsync(_cts.Token);
    }

    public void Dispose()
    {
        _cts.Cancel(); _cts.Dispose();
        _transport?.Dispose();
    }

    // ═══════════ MCP Tool entry points ═══════════

    public Task<SessionInfo> CreateAsync(string exe, string? args, string? workDir, bool stopAtEntry) => PostAsync<SessionInfo>(new SessionOperation.Create(exe, args, workDir, stopAtEntry, new()));
    public Task<SessionInfo> AttachAsync(int pid) => PostAsync<SessionInfo>(new SessionOperation.Attach(pid, new()));
    public Task<SessionInfo> LoadDumpAsync(string path) => PostAsync<SessionInfo>(new SessionOperation.LoadDump(path, new()));
    public Task<string> DetachAsync() => PostAsync<string>(new SessionOperation.Detach(new()));
    public Task<string> TerminateAsync() => PostAsync<string>(new SessionOperation.Terminate(new()));
    public Task<SessionStatus> StatusAsync() => PostAsync<SessionStatus>(new SessionOperation.Status(new()));
    public Task<string> GoAsync(int timeoutMs) => PostAsync<string>(new SessionOperation.Go(timeoutMs, new()));
    public Task<string> StepIntoAsync() => PostAsync<string>(new SessionOperation.StepInto(new()));
    public Task<string> StepOverAsync() => PostAsync<string>(new SessionOperation.StepOver(new()));
    public Task<string> StepOutAsync() => PostAsync<string>(new SessionOperation.StepOut(new()));
    public Task<string> GoToAsync(string location) => PostAsync<string>(new SessionOperation.GoTo(location, new()));
    public async Task<BreakpointConfig> SetBreakpointAsync(string loc, bool capture, string action, string? cond) { var tcs = new TaskCompletionSource<BreakpointConfig>(); await _channel.Writer.WriteAsync(new SessionOperation.SetBreakpoint(loc, capture, action, cond, tcs)); return await tcs.Task; }
    public async Task<BreakpointConfig> SetHardwareBreakpointAsync(string address, string access, int size, bool capture) { var tcs = new TaskCompletionSource<BreakpointConfig>(); await _channel.Writer.WriteAsync(new SessionOperation.SetHardwareBreakpoint(address, access, size, capture, tcs)); return await tcs.Task; }
    public Task<bool> RemoveBreakpointAsync(string id) => PostAsync<bool>(new SessionOperation.RemoveBreakpoint(id, new()));
    public Task<bool> EnableBreakpointAsync(string id, bool enabled) => PostAsync<bool>(new SessionOperation.EnableBreakpoint(id, enabled, new()));
    public Task<Dictionary<string, string>> GetRegistersAsync() => PostAsync<Dictionary<string, string>>(new SessionOperation.GetRegisters(new()));
    public Task<MemoryData> ReadMemoryAsync(string address, int size) => PostAsync<MemoryData>(new SessionOperation.ReadMemory(address, size, new()));
    public Task<List<GdbMi.FrameInfo>> GetCallStackAsync(int maxFrames) => PostAsync<List<GdbMi.FrameInfo>>(new SessionOperation.GetCallStack(maxFrames, new()));
    public Task<List<ThreadInfo>> ListThreadsAsync() => PostAsync<List<ThreadInfo>>(new SessionOperation.ListThreads(new()));
    public Task<List<VariableInfo>> GetLocalVariablesAsync(int frameIndex) => PostAsync<List<VariableInfo>>(new SessionOperation.GetLocalVariables(frameIndex, new()));
    public Task<ProgramCounterInfo> GetProgramCounterAsync() => PostAsync<ProgramCounterInfo>(new SessionOperation.GetProgramCounter(new()));
    public Task<CaptureResult> CaptureStateAsync() => PostAsync<CaptureResult>(new SessionOperation.CaptureState(new()));
    public Task<SymbolInfo> ResolveSymbolAsync(string name) => PostAsync<SymbolInfo>(new SessionOperation.ResolveSymbol(name, new()));
    public Task<SymbolInfo> AddressToSymbolAsync(string address) => PostAsync<SymbolInfo>(new SessionOperation.AddressToSymbol(address, new()));
    public Task<List<SymbolInfo>> FindSymbolsAsync(string pattern) => PostAsync<List<SymbolInfo>>(new SessionOperation.FindSymbols(pattern, new()));
    public Task<List<DisassemblyLine>> DisassembleAsync(string address, int count) => PostAsync<List<DisassemblyLine>>(new SessionOperation.Disassemble(address, count, new()));
    public Task<List<ModuleInfo>> ListModulesAsync() => PostAsync<List<ModuleInfo>>(new SessionOperation.ListModules(new()));
    public Task<string> RawGdbAsync(string command) => PostAsync<string>(new SessionOperation.RawGdb(command, new()));

    private async Task<T> PostAsync<T>(SessionOperation op) where T : notnull
    {
        var tcs = (TaskCompletionSource<T>)op.GetType().GetProperty("Completion")!.GetValue(op)!;
        await _channel.Writer.WriteAsync(op);
        return await tcs.Task;
    }

    // ═══════════ Consumer Loop ═══════════

    private async Task ConsumerLoopAsync(CancellationToken ct)
    {
        try { while (!ct.IsCancellationRequested) { var op = await _channel.Reader.ReadAsync(ct); await ProcessOperationAsync(op); } }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Consumer loop error"); }
    }

    private async Task ProcessOperationAsync(SessionOperation op)
    {
        switch (op)
        {
            case SessionOperation.Create c: await HandleCreate(c); break;
            case SessionOperation.Attach a: a.Completion.TrySetResult(new("attach", null, null)); break;
            case SessionOperation.LoadDump ld: await HandleLoadDump(ld); break;
            case SessionOperation.Go g: await HandleGo(g); break;
            case SessionOperation.StepInto si: await HandleStep(si.Completion, () => _cmd!.ExecStep(_currentThread)); break;
            case SessionOperation.StepOver so: await HandleStep(so.Completion, () => _cmd!.ExecNext(_currentThread)); break;
            case SessionOperation.StepOut so2: await HandleStep(so2.Completion, () => _cmd!.ExecFinish(_currentThread)); break;
            case SessionOperation.GoTo gt: await HandleGoTo(gt); break;
            case SessionOperation.SetBreakpoint sb: await HandleSetBreakpoint(sb); break;
            case SessionOperation.SetHardwareBreakpoint hb: await HandleSetHwBreakpoint(hb); break;
            case SessionOperation.RemoveBreakpoint rb: await HandleRemoveBp(rb); break;
            case SessionOperation.EnableBreakpoint eb: await HandleEnableBp(eb); break;
            case SessionOperation.ListBreakpoints lb: lb.Completion.TrySetResult(_bpManager.GetAll()); break;
            case SessionOperation.GetRegisters gr: await HandleGetRegisters(gr); break;
            case SessionOperation.ReadMemory rm: await HandleReadMemory(rm); break;
            case SessionOperation.GetCallStack gcs: await HandleGetCallStack(gcs); break;
            case SessionOperation.ListThreads lt: await HandleListThreads(lt); break;
            case SessionOperation.GetLocalVariables glv: await HandleGetLocalVariables(glv); break;
            case SessionOperation.GetProgramCounter gpc: await HandleGetProgramCounter(gpc); break;
            case SessionOperation.CaptureState cs: cs.Completion.TrySetResult(await CaptureAsync("manual", "manual")); break;
            case SessionOperation.GetCaptures gc: gc.Completion.TrySetResult(_captures.GetAll()); break;
            case SessionOperation.ClearCaptures cc: _captures.Clear(); cc.Completion.TrySetResult(true); break;
            case SessionOperation.ResolveSymbol rs: await HandleResolveSymbol(rs); break;
            case SessionOperation.AddressToSymbol a2s: await HandleAddressToSymbol(a2s); break;
            case SessionOperation.FindSymbols fs: await HandleFindSymbols(fs); break;
            case SessionOperation.Disassemble d: await HandleDisassemble(d); break;
            case SessionOperation.ListModules lm: await HandleListModules(lm); break;
            case SessionOperation.RawGdb rg: await HandleRawGdb(rg); break;
            case SessionOperation.Status st: st.Completion.TrySetResult(new(State.ToString())); break;
            case SessionOperation.Detach d2: await HandleDetach(d2); break;
            case SessionOperation.Terminate t: await HandleTerminate(t); break;
        }
    }

    // ═══════════ Background ReadLoop ═══════════

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _transport is not null && !_transport.IsClosed)
            {
                var line = await _transport.ReadLineAsync(ct);
                if (line is null) break;

                // Intercept *stopped for auto-capture BEFORE feeding to GdbMiClient
                if (line.StartsWith('*'))
                {
                    var content = line[1..].Trim();
                    if (content.StartsWith("stopped"))
                    {
                        await OnStopped(line);
                        if (_waitingForStop is null)
                            continue; // go-action: keep reading, Agent doesn't know
                    }
                }
                // Feed all lines to GdbMiClient for token matching / state tracking
                _client?.ProcessLine(line);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ═══════════ Handlers ═══════════

    private async Task HandleCreate(SessionOperation.Create c)
    {
        _transport = new GdbMi.LocalTransport("gdb", "--interpreter=mi3", _logger as Microsoft.Extensions.Logging.ILogger);
        _client = new GdbMi.GdbMiClient(_transport, _logger as Microsoft.Extensions.Logging.ILogger<GdbMi.GdbMiClient>);
        _cmd = _client.Cmd;

        await _client.ConnectAsync(CancellationToken.None);

        // Start background ReadLoop BEFORE any commands (so responses are read)
        _readLoopCts = new CancellationTokenSource();
        _readLoopTask = ReadLoopAsync(_readLoopCts.Token);

        await _cmd.EnableTargetAsyncOption();
        await _cmd.FileExecAndSymbols(c.Executable);
        if (c.Arguments is not null)
            await _client.ConsoleCmdAsync($"set args {c.Arguments}", allowWhileRunning: false);
        if (c.StopAtEntry)
            await _cmd.BreakInsertFunction("main");
        await _cmd.ExecRun();

        // First *stopped completes the Create operation
        _waitingForFirstStop = c.Completion;
    }

    private async Task HandleGo(SessionOperation.Go g)
    {
        _waitingForStop = g.Completion;
        if (g.TimeoutMs > 0)
        {
            _goTimeoutCts = new CancellationTokenSource(g.TimeoutMs);
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(g.TimeoutMs, _goTimeoutCts.Token); await _client!.ExecuteAsync(new GdbMi.MICommand("-exec-interrupt", "")); }
                catch { }
            });
        }
        await _cmd!.ExecContinue();
    }

    private async Task HandleStep(TaskCompletionSource<string> tcs, Func<Task> stepAction)
    {
        _waitingForStop = tcs;
        await stepAction();
    }

    private async Task HandleGoTo(SessionOperation.GoTo gt)
    {
        _waitingForStop = gt.Completion;
        await _cmd!.BreakInsertRaw($"-t {gt.Location}");
        await _cmd.ExecContinue();
    }

    private async Task HandleSetBreakpoint(SessionOperation.SetBreakpoint sb)
    {
        var result = await _cmd!.BreakInsert(sb.Location, 0, condition: sb.Condition, enabled: true);
        var bkpt = result.Find<GdbMi.TupleValue>("bkpt");
        var number = bkpt.FindString("number");
        _bpManager.Register(number, new(number, sb.Location, sb.Capture, sb.Action, sb.Condition, true));
        sb.Completion.TrySetResult(new(number, sb.Location, sb.Capture, sb.Action, sb.Condition, true));
    }

    private async Task HandleSetHwBreakpoint(SessionOperation.SetHardwareBreakpoint hb)
    {
        var result = await _cmd!.BreakWatch(hb.Address, (uint)hb.Size, hb.Access);
        var bkpt = result.Find<GdbMi.TupleValue>("wpt");
        var number = bkpt.FindString("number");
        var loc = $"[{hb.Access} @ {hb.Address}, size={hb.Size}]";
        _bpManager.Register(number, new(number, loc, hb.Capture, "break", null, true));
        hb.Completion.TrySetResult(new(number, loc, hb.Capture, "break", null, true));
    }

    private async Task HandleRemoveBp(SessionOperation.RemoveBreakpoint rb)
    { await _cmd!.BreakDelete(rb.Id); _bpManager.Remove(rb.Id); rb.Completion.TrySetResult(true); }

    private async Task HandleEnableBp(SessionOperation.EnableBreakpoint eb)
    { await _cmd!.BreakEnable(eb.Id, eb.Enabled); _bpManager.SetEnabled(eb.Id, eb.Enabled); eb.Completion.TrySetResult(true); }

    private async Task HandleGetRegisters(SessionOperation.GetRegisters gr)
    {
        var regs = await _cmd!.DataListRegisterValues(_currentThread);
        var dict = new Dictionary<string, string>();
        foreach (var r in regs) dict[r.TryFindString("number") ?? "?"] = r.TryFindString("value") ?? "?";
        gr.Completion.TrySetResult(dict);
    }

    private async Task HandleReadMemory(SessionOperation.ReadMemory rm)
    {
        var cleanAddr = rm.Address.Trim();
        var spaceIdx = cleanAddr.IndexOf(' ');
        if (spaceIdx > 0) cleanAddr = cleanAddr[..spaceIdx];
        if (cleanAddr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleanAddr = cleanAddr[2..];

        var result = await _client!.ExecuteAsync(
            new GdbMi.MICommand("-data-read-memory",
                $"0x{cleanAddr} x 1 1 {rm.Size}"));

        // GDB returns: memory={addr="...",data=["0x00","0x00"...]}
        // Parse adaptively: memory can be TupleValue (single row) or ValueListValue (multiple)
        var memoryVal = result.Find("memory");
        GdbMi.ValueListValue dataList;
        if (memoryVal is GdbMi.TupleValue tv)
            dataList = tv.Find<GdbMi.ValueListValue>("data");
        else if (memoryVal is GdbMi.ValueListValue vl && vl.Length > 0 && vl.AsArray<GdbMi.TupleValue>()[0] is GdbMi.TupleValue first)
            dataList = first.Find<GdbMi.ValueListValue>("data");
        else
        {
            rm.Completion.TrySetResult(new($"0x{cleanAddr}", 0, "", Array.Empty<byte>(), ""));
            return;
        }

        var hexStrings = dataList.AsStrings;
        var bytes = hexStrings.Select(s => byte.Parse(s.AsSpan(2),
            System.Globalization.NumberStyles.HexNumber)).ToArray();
        var hex = FormatHexDump($"0x{cleanAddr}", bytes);
        var ascii = BytesToAscii(bytes);
        rm.Completion.TrySetResult(new($"0x{cleanAddr}", bytes.Length, hex, bytes, ascii));
    }

    private static string FormatHexDump(string addr, byte[] bytes)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(addr).Append(": ");
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0 && i % 16 == 0) sb.Append('\n').Append(new string(' ', addr.Length + 2));
            sb.Append(bytes[i].ToString("x2")).Append(' ');
        }
        return sb.ToString().Trim();
    }

    private static string FormatHexDump(string addr, string hexContents, int size)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(addr).Append(": ");
        for (int i = 0; i < hexContents.Length && i / 2 < size; i += 2)
        {
            if (i > 0 && i % 32 == 0) sb.Append('\n').Append(new string(' ', addr.Length + 2));
            sb.Append(hexContents.Substring(i, Math.Min(2, hexContents.Length - i))).Append(' ');
        }
        return sb.ToString().Trim();
    }

    private static byte[] HexToBytes(string hex, int maxBytes)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < hex.Length - 1 && bytes.Count < maxBytes; i += 2)
        {
            if (byte.TryParse(hex.AsSpan(i, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                bytes.Add(b);
        }
        return bytes.ToArray();
    }

    private static string BytesToAscii(byte[] bytes)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in bytes)
            sb.Append(b >= 32 && b < 127 ? (char)b : '.');
        return sb.ToString();
    }

    private async Task HandleGetCallStack(SessionOperation.GetCallStack gcs)
    { var frames = await _cmd!.StackListFrames(_currentThread, 0, (uint)gcs.MaxFrames); gcs.Completion.TrySetResult(FramesToList(frames)); }

    private async Task HandleListThreads(SessionOperation.ListThreads lt)
    {
        var result = await _cmd!.ThreadInfo();
        var threads = new List<ThreadInfo>();
        var tl = result.Find("threads");
        var threadTuples = tl switch
        {
            GdbMi.ValueListValue vl => vl.AsArray<GdbMi.TupleValue>(),
            GdbMi.ResultListValue rl => rl.FindAll<GdbMi.TupleValue>("thread"),
            _ => Array.Empty<GdbMi.TupleValue>()
        };
        foreach (GdbMi.TupleValue t in threadTuples)
            threads.Add(new(t.FindInt("id"), t.FindString("id") == result.TryFindString("current-thread-id")));
        lt.Completion.TrySetResult(threads);
    }

    private async Task HandleGetLocalVariables(SessionOperation.GetLocalVariables glv)
    { glv.Completion.TrySetResult(ParseVariables(await _cmd!.StackListLocals(1, _currentThread, (uint)glv.FrameIndex))); }

    private async Task HandleGetProgramCounter(SessionOperation.GetProgramCounter gpc) { try { var f = (await _cmd!.StackInfoFrame()).Find<GdbMi.TupleValue>("frame"); gpc.Completion.TrySetResult(new(f.TryFindString("addr") ?? "", f.TryFindString("func") ?? "", "")); } catch { gpc.Completion.TrySetResult(new("?", "?", "")); } }

    /// <summary>Run a CLI command and capture console output (~ lines). Uses SendAndReadInline pattern.</summary>
    /// <summary>Run a CLI command and capture console output. Pauses background ReadLoop to avoid read conflicts.</summary>
    private async Task<string> CliCommandAsync(string cmd)
    {
        // Pause the background ReadLoop so we can safely read responses ourselves
        _readLoopCts?.Cancel();
        if (_readLoopTask is not null) { try { await _readLoopTask; } catch { } }

        try
        {
            uint token = Interlocked.Increment(ref _nextToken);
            var escaped = cmd.Replace("\"", "\\\"");
            await _transport!.SendAsync($"{token}-interpreter-exec console \"{escaped}\"\n", CancellationToken.None);

            var output = new System.Text.StringBuilder();
            while (!_transport.IsClosed)
            {
                var line = await _transport.ReadLineAsync(CancellationToken.None);
                if (line is null) break;
                if (line.StartsWith($"{token}^")) break;
                if (line.StartsWith('~'))
                {
                    var decoded = new GdbMi.MIResultParser().ParseCString(line[1..].Trim());
                    output.Append(decoded);
                }
                else { _client?.ProcessLine(line); }
            }
            return output.ToString().Trim();
        }
        finally
        {
            // Restart ReadLoop
            _readLoopCts = new CancellationTokenSource();
            _readLoopTask = ReadLoopAsync(_readLoopCts.Token);
        }
    }

    private async Task HandleResolveSymbol(SessionOperation.ResolveSymbol rs) { _ = rs; try { var r = await CliCommandAsync($"info address {rs.Name}"); rs.Completion.TrySetResult(new(rs.Name, ExtractAddress(r) ?? "unknown")); } catch { rs.Completion.TrySetResult(new(rs.Name, "unknown")); } }
    private async Task HandleAddressToSymbol(SessionOperation.AddressToSymbol a2s) { try { var r = await CliCommandAsync($"info symbol {a2s.Address}"); var sym = r.Split('\n')[0].Trim(); a2s.Completion.TrySetResult(new(sym.Length > 0 ? sym : "??", a2s.Address)); } catch { a2s.Completion.TrySetResult(new("??", a2s.Address)); } }
    private async Task HandleFindSymbols(SessionOperation.FindSymbols fs) { try { var r = await CliCommandAsync($"info functions {fs.Pattern}"); fs.Completion.TrySetResult(ParseSymbolLines(r)); } catch { fs.Completion.TrySetResult(new()); } }
    private async Task HandleDisassemble(SessionOperation.Disassemble d) { d.Completion.TrySetResult(ParseDisassembly(await _client!.ConsoleCmdAsync($"disassemble {d.Address},+{d.Count * 4}", allowWhileRunning: false))); }
    private async Task HandleListModules(SessionOperation.ListModules lm) { try { var r = await CliCommandAsync("info sharedlibrary"); lm.Completion.TrySetResult(ParseSharedLibs(r)); } catch { lm.Completion.TrySetResult(new()); } }

    private static List<ModuleInfo> ParseSharedLibs(string output)
    {
        var list = new List<ModuleInfo>();
        using var sr = new StringReader(output);
        while (sr.ReadLine() is string line)
        {
            if (!line.StartsWith("0x")) continue;
            // Format: "0xSTART  0xEND  Yes/No     /path/to/lib"
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
                list.Add(new ModuleInfo(parts[^1], parts[0], 0));
        }
        return list;
    }
    private async Task HandleRawGdb(SessionOperation.RawGdb rg)
    {
        if (rg.Command.StartsWith('-'))
        {
            // MI command: use ExecuteAsync, result comes in ^done
            var results = await _client!.ExecuteAsync(new GdbMi.MICommand(rg.Command, ""));
            // For -data-evaluate-expression, return just the value. Otherwise dump all fields.
            string output = results.TryFindString("value")
                ?? string.Join("; ", results.Content.Select(c => $"{c.Name}={c.Value}"));
            rg.Completion.TrySetResult(output);
        }
        else
        {
            // CLI command: use ConsoleCmdAsync
            rg.Completion.TrySetResult(await _client!.ConsoleCmdAsync(rg.Command, allowWhileRunning: true));
        }
    }

    private async Task HandleLoadDump(SessionOperation.LoadDump ld)
    {
        _transport = new GdbMi.LocalTransport("gdb", "--interpreter=mi3", _logger as Microsoft.Extensions.Logging.ILogger);
        _client = new GdbMi.GdbMiClient(_transport, _logger as Microsoft.Extensions.Logging.ILogger<GdbMi.GdbMiClient>);
        _cmd = _client.Cmd;
        await _client.ConnectAsync(CancellationToken.None);
        _readLoopCts = new CancellationTokenSource();
        _readLoopTask = ReadLoopAsync(_readLoopCts.Token);

        // GDB can load core dumps with just -target-select core
        // The executable is embedded in the core file
        await _client.ExecuteAsync(new GdbMi.MICommand("-target-select", $"core {ld.Path}"));
        ld.Completion.TrySetResult(new("load_dump", _transport.DebuggerPid, null));
    }

    private async Task HandleDetach(SessionOperation.Detach d) { await _cmd!.TargetDetach(); CleanupSession(); d.Completion.TrySetResult("detached"); }
    private async Task HandleTerminate(SessionOperation.Terminate t) { await _cmd!.Terminate(); CleanupSession(); t.Completion.TrySetResult("terminated"); }

    // ═══════════ *stopped interception (called from ReadLoop) ═══════════

    private async Task OnStopped(string line)
    {
        _client!.ProcessLine(line);
        var content = line[1..].Trim();

        string reason = "";
        string? bkptno = null;
        if (content.StartsWith("stopped,"))
        {
            var parser = new GdbMi.MIResultParser();
            var results = parser.ParseResultList(content["stopped,".Length..]);
            reason = results.TryFindString("reason");
            bkptno = results.TryFindString("bkptno");
        }

        // Handle first stop after create
        if (_waitingForFirstStop is not null)
        {
            var createCompletion = _waitingForFirstStop;
            _waitingForFirstStop = null;
            createCompletion.TrySetResult(new("create", _transport?.DebuggerPid, null));
            return;
        }

        if ((reason == "breakpoint-hit" || reason == "watchpoint-trigger") && bkptno is not null)
        {
            var (shouldCapture, shouldContinue) = _bpManager.OnHit(bkptno);
            if (shouldCapture)
                _captures.Add(await CaptureAsync(bkptno, _bpManager.FindByNumber(bkptno)?.Location ?? "unknown"));
            if (shouldContinue)
            {
                _goTimeoutCts?.Cancel();
                await SendAndReadInline("-exec-continue");
                return;
            }
        }

        _goTimeoutCts?.Cancel();
        var tcs = _waitingForStop;
        _waitingForStop = null;
        tcs?.TrySetResult(reason.Length > 0 ? reason : "stopped");
    }

    /// <summary>
    /// Send an MI command and read the response inline. Used during capture
    /// (which runs in the ReadLoop thread) to avoid deadlocking on ExecuteAsync.
    /// </summary>
    private async Task<GdbMi.Results> SendAndReadInline(string miCmd)
    {
        uint token = Interlocked.Increment(ref _nextToken);
        await _transport!.SendAsync($"{token}{miCmd}\n", CancellationToken.None);

        while (!_transport.IsClosed)
        {
            var line = await _transport.ReadLineAsync(CancellationToken.None);
            if (line is null) break;

            if (line.StartsWith($"{token}^"))
            {
                var rest = line[(token.ToString().Length + 1)..].Trim();
                return new GdbMi.MIResultParser().ParseCommandOutput(rest);
            }
            // Feed other lines to GdbMiClient so state stays updated
            _client?.ProcessLine(line);
            // If we see a *stopped while waiting for a capture command, we're in trouble
        }
        return new GdbMi.Results(GdbMi.ResultClass.Error);
    }
    private uint _nextToken = 10000; // separate token space for capture commands

    private async Task<CaptureResult> CaptureAsync(string bpNumber, string location)
    {
        Dictionary<string, string> r = new();
        try { var res = await SendAndReadInline($"-data-list-register-values x"); foreach (GdbMi.TupleValue x in res.Find<GdbMi.ValueListValue>("register-values").AsArray<GdbMi.TupleValue>()) r[x.TryFindString("number") ?? "?"] = x.TryFindString("value") ?? "?"; } catch { }

        ProgramCounterInfo p = new("", "", "");
        try { var res = await SendAndReadInline("-stack-info-frame"); var f = res.Find<GdbMi.TupleValue>("frame"); p = new(f.TryFindString("addr") ?? "", f.TryFindString("func") ?? "", ""); } catch { }

        List<GdbMi.FrameInfo> s = new();
        try { var res = await SendAndReadInline($"-stack-list-frames 0 20"); var stack = res.Find<GdbMi.ListValue>("stack"); if (stack is GdbMi.ResultListValue rv) s = FramesToList(rv.FindAll<GdbMi.TupleValue>("frame")); } catch { }

        List<VariableInfo> l = new();
        try { var res = await SendAndReadInline($"-stack-list-locals --thread 1 --frame 0 1"); l = ParseVariables(res.Find("locals")); } catch { }

        return new(bpNumber, location, r, p, s, null, l, DateTimeOffset.UtcNow);
    }

    // ═══════════ Helpers ═══════════

    private void CleanupSession() { _bpManager.Clear(); _captures.Clear(); _transport?.Close(); _client = null; _cmd = null; _transport = null; }

    private static List<GdbMi.FrameInfo> FramesToList(GdbMi.TupleValue[] frames) => frames.Select(f => new GdbMi.FrameInfo(f.TryFindString("level") ?? "", f.TryFindString("addr"), f.TryFindString("func"), f.TryFindString("file"), f.TryFindUint("line") is { } vl ? (int?)vl : null)).ToList();

    private static List<VariableInfo> ParseVariables(GdbMi.ResultValue locals) { var r = new List<VariableInfo>(); if (locals is GdbMi.ValueListValue v) foreach (GdbMi.TupleValue t in v.AsArray<GdbMi.TupleValue>()) r.Add(new(t.TryFindString("name") ?? "?", t.TryFindString("type") ?? "?", t.TryFindString("value") ?? "?")); return r; }

    private static string? ExtractAddress(string o) { var i = o.IndexOf("0x", StringComparison.Ordinal); if (i < 0) return null; var e = o.IndexOf(' ', i); return e < 0 ? o[i..] : o[i..e]; }

    private static List<SymbolInfo> ParseSymbolLines(string o) { var l = new List<SymbolInfo>(); using var sr = new StringReader(o); while (sr.ReadLine() is string ln) { var i = ln.IndexOf("0x", StringComparison.Ordinal); if (i > 0) l.Add(new(ln[..i].Trim(), ln[i..].Trim())); } return l; }

    private static List<DisassemblyLine> ParseDisassembly(string o) { var l = new List<DisassemblyLine>(); using var sr = new StringReader(o); while (sr.ReadLine() is string ln) { ln = ln.Trim(); if (string.IsNullOrEmpty(ln) || ln.StartsWith("Dump") || ln.StartsWith("End")) continue; var sp = ln.IndexOf(' '); if (sp < 0) continue; var a = ln[..sp].TrimEnd(':'); var r = ln[(sp + 1)..]; var t = r.IndexOf('\t'); l.Add(new(a, t > 0 ? r[..t].Trim() : "", t > 0 ? r[(t + 1)..].Trim() : r.Trim())); } return l; }
}
