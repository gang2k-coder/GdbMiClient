using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace GdbMi;

/// <summary>
/// Core MI protocol engine. Single-threaded — command execution is serialized upstream.
/// Ported from MICore/Debugger.cs (~1700 → ~350 lines).
/// </summary>
public class GdbMiClient : IDisposable
{
    private readonly ITransport _transport;
    private readonly MIResultParser _parser;
    private readonly ILogger? _logger;

    private uint _nextToken = 1000;
    private readonly Dictionary<uint, PendingCommand> _pending = new();
    private readonly Channel<string> _outputChannel = Channel.CreateUnbounded<string>();
    private readonly Channel<StopEvent> _stopChannel = Channel.CreateUnbounded<StopEvent>();

    public DebuggerState State { get; internal set; } = DebuggerState.NotConnected;
    public bool IsConnected => State != DebuggerState.NotConnected;

    public GdbMiClient(ITransport transport, ILogger<GdbMiClient>? logger = null)
    {
        _transport = transport;
        _logger = logger;
        _parser = new MIResultParser(logger);
    }

    // ═══════════ Lifecycle ═══════════

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _transport.ConnectAsync(ct);
        State = DebuggerState.Running;
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        try { await ExecuteAsync(new MICommand("-gdb-exit", "")); }
        catch (ObjectDisposedException) { }
        _transport.Close();
        State = DebuggerState.Exited;
    }

    public void Dispose()
    {
        _transport.Close();
        _outputChannel.Writer.TryComplete();
    }

    // ═══════════ Command execution ═══════════

    public async Task<Results> ExecuteAsync(MICommand command, CancellationToken ct = default)
    {
        if (State == DebuggerState.Exited)
            throw new ObjectDisposedException(nameof(GdbMiClient));

        uint token = ++_nextToken;
        var tcs = new TaskCompletionSource<Results>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[token] = new PendingCommand(command.MiText, tcs);

        var line = $"{token}{command.MiText}";
        await _transport.SendAsync(line, ct);

        return await tcs.Task;
    }

    public async Task<string> ConsoleCmdAsync(string cmd, bool allowWhileRunning, CancellationToken ct = default)
    {
        if (!allowWhileRunning && State != DebuggerState.Stopped)
            throw new InvalidOperationException("Process must be stopped");
        string escaped = cmd.Replace("\"", "\\\"");
        var results = await ExecuteAsync(
            new MICommand("-interpreter-exec", $"console \"{escaped}\""), ct);
        return results.ResultClass == ResultClass.Done ? "" : results.FindString("msg");
    }

    // ═══════════ Line processing — ReadLoop thread ═══════════

    public void ProcessLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        string? tokenStr = ParseToken(ref line);
        if (line.Length == 0) return;

        char prefix = line[0];
        string content = line.Substring(1).Trim();

        switch (prefix)
        {
            case '^': HandleResult(content, tokenStr); break;
            case '*': HandleOutOfBand(content); break;
            case '~': case '@': case '&': HandleOutput(content); break;
            case '=': HandleNotification(content); break;
            default: HandleOutput(line + '\n'); break;
        }
    }

    // ═══════════ Async event providers ═══════════

    public async Task<StopEvent> WaitForStopAsync(CancellationToken ct = default)
    {
        return await _stopChannel.Reader.ReadAsync(ct);
    }

    public IAsyncEnumerable<string> GetOutputStream(CancellationToken ct = default)
        => _outputChannel.Reader.ReadAllAsync(ct);

    // ═══════════ Internal handlers ═══════════

    private void HandleResult(string content, string? tokenStr)
    {
        var results = _parser.ParseCommandOutput(content);
        if (tokenStr is not null && uint.TryParse(tokenStr, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out uint token))
        {
            if (_pending.Remove(token, out var cmd))
                cmd.Completion.TrySetResult(results);
        }
    }

    private void HandleOutOfBand(string content)
    {
        if (content.StartsWith("stopped", StringComparison.Ordinal))
        {
            var results = content.StartsWith("stopped,", StringComparison.Ordinal)
                ? _parser.ParseResultList(content.Substring(8))
                : new Results(ResultClass.None);

            State = DebuggerState.Stopped;
            var stopEvent = ParseStopEvent(results);
            _stopChannel.Writer.TryWrite(stopEvent);
        }
        else if (content.StartsWith("running", StringComparison.Ordinal))
        {
            State = DebuggerState.Running;
        }
    }

    private void HandleOutput(string content)
    {
        string decoded = _parser.ParseCString(content);
        _outputChannel.Writer.TryWrite(decoded);
    }

    private void HandleNotification(string content)
    {
        _logger?.LogDebug("MI notification: {Content}", content);
    }

    // ═══════════ Helpers ═══════════

    private static string? ParseToken(ref string cmd)
    {
        if (cmd.Length == 0 || !char.IsDigit(cmd[0])) return null;
        int i;
        for (i = 1; i < cmd.Length; i++)
            if (!char.IsDigit(cmd[i])) break;
        if (i >= cmd.Length) return null;
        string token = cmd.Substring(0, i);
        cmd = cmd.Substring(i);
        return token;
    }

    private static StopEvent ParseStopEvent(Results results)
    {
        string reason = results.TryFindString("reason");
        var frameResult = results.TryFind<TupleValue>("frame");
        return new StopEvent(
            Reason: reason,
            BreakpointNumber: reason == "breakpoint-hit" ? results.TryFindString("bkptno") : null,
            SignalName: reason == "signal-received" ? results.TryFindString("signal-name") : null,
            ThreadId: results.TryFindUint("thread-id") is { } tid ? (int?)tid : null,
            AllThreadsStopped: results.TryFindString("stopped-threads") == "all",
            Frame: frameResult is not null
                ? new FrameInfo(
                    Level: frameResult.TryFindString("level"),
                    Address: frameResult.TryFindString("addr"),
                    FunctionName: frameResult.TryFindString("func"),
                    File: frameResult.TryFindString("file"),
                    Line: frameResult.TryFindUint("line") is { } l ? (int?)l : null)
                : null
        );
    }

    private record PendingCommand(string CommandText, TaskCompletionSource<Results> Completion);
}

public enum DebuggerState { NotConnected, Running, Stopped, Exited }
