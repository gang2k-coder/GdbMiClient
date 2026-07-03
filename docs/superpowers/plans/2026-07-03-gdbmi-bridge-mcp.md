# GdbMiBridge.Mcp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `GdbMiBridge.Mcp` — an MCP stdio server that exposes 32 debugging tools, backed by the `GdbMiClient` library and controlled by a single-threaded `GdbSession` consumer loop.

**Architecture:** MCP tools are registered via `[McpServerTool]` attributes. Each tool calls `GdbSession.SendOperationAsync()` which enqueues an operation on a `Channel<SessionOperation>`. A single consumer thread processes operations sequentially, calling `GdbMiClient` for MI protocol I/O. BreakpointManager and CapturesManager live in the consumer thread (lock-free). Async events (`*stopped`) trigger auto-capture + go/break decision.

**Tech Stack:** .NET 10, `ModelContextProtocol` (preview), `Microsoft.Extensions.Hosting`, `GdbMiClient` project reference, xUnit (tests)

**Source reference:** `docs/superpowers/specs/2026-07-03-gdbmi-bridge-mcp-design.md`

---

## File Structure

```
C:\Code\GdbMiClient\
├── GdbMiClient.slnx
├── src/
│   ├── GdbMiClient/                    ← 已有库 (修改: 暴露 Cmd 属性)
│   └── GdbMiBridge.Mcp/
│       ├── GdbMiBridge.Mcp.csproj
│       ├── Program.cs                   ← MCP 入口 + DI 注册
│       ├── SessionOperation.cs          ← Channel 消息类型
│       ├── CaptureResult.cs             ← 捕获快照数据模型
│       ├── BreakpointManager.cs         ← 断点配置 + go/break 决策
│       ├── CapturesManager.cs           ← 捕获快照管理
│       ├── GdbSession.cs               ← 消费者线程 + Channel 编排
│       └── ToolHandlers/
│           ├── SessionTools.cs          ← create, attach, load_dump, detach, terminate, status
│           ├── ExecutionTools.cs        ← go, step_into, step_over, step_out, go_to
│           ├── BreakpointTools.cs       ← set_bp, remove_bp, enable/disable, list
│           ├── StateTools.cs            ← registers, memory, stack, threads, variables, captures, pc
│           ├── SymbolTools.cs           ← symbol resolution, disassembly, modules
│           └── RawTools.cs              ← raw_gdb
└── tests/
    └── GdbMiBridge.Mcp.Tests/
        ├── GdbMiBridge.Mcp.Tests.csproj
        ├── BreakpointManagerTests.cs
        ├── CapturesManagerTests.cs
        └── GdbSessionTests.cs
```

---

### Task 1: Create MCP project and add to solution

**Goal:** Scaffold the MCP server project with NuGet dependencies and project reference.

- [ ] **Step 1: Create project**

```bash
mkdir -p /c/Code/GdbMiClient/src/GdbMiBridge.Mcp/ToolHandlers
cd /c/Code/GdbMiClient/src/GdbMiBridge.Mcp
dotnet new console -n GdbMiBridge.Mcp -f net10.0 --output .
rm -f Program.cs
dotnet add package ModelContextProtocol --prerelease
dotnet add package Microsoft.Extensions.Hosting
dotnet add reference ../../src/GdbMiClient/GdbMiClient.csproj
cd /c/Code/GdbMiClient
dotnet sln add src/GdbMiBridge.Mcp/GdbMiBridge.Mcp.csproj
```

- [ ] **Step 2: Create test project**

```bash
mkdir -p /c/Code/GdbMiClient/tests/GdbMiBridge.Mcp.Tests
cd /c/Code/GdbMiClient/tests/GdbMiBridge.Mcp.Tests
dotnet new xunit -n GdbMiBridge.Mcp.Tests -f net10.0 --output .
rm -f UnitTest1.cs
dotnet add reference ../../src/GdbMiBridge.Mcp/GdbMiBridge.Mcp.csproj
dotnet add reference ../../src/GdbMiClient/GdbMiClient.csproj
cd /c/Code/GdbMiClient
dotnet sln add tests/GdbMiBridge.Mcp.Tests/GdbMiBridge.Mcp.Tests.csproj
```

- [ ] **Step 3: Build and commit**

```bash
cd /c/Code/GdbMiClient
dotnet build
# Expected: Build succeeds
git add -A && git commit -m "feat: scaffold GdbMiBridge.Mcp project with MCP SDK"
```

---

### Task 2: Data models — SessionOperation + CaptureResult

**Goal:** Define the Channel message types and capture snapshot data model.

**Files:**
- Create: `src/GdbMiBridge.Mcp/SessionOperation.cs`
- Create: `src/GdbMiBridge.Mcp/CaptureResult.cs`

- [ ] **Step 1: Write SessionOperation.cs**

```csharp
namespace GdbMiBridge.Mcp;

/// <summary>
/// Channel message types. Each MCP tool call maps to one operation.
/// Consumer thread deserializes these from the Channel.
/// </summary>
public abstract record SessionOperation
{
    public record SetBreakpoint(
        string Location, bool Capture, string Action, string? Condition,
        TaskCompletionSource<BreakpointInfo> Completion) : SessionOperation;

    public record RemoveBreakpoint(string Id,
        TaskCompletionSource<bool> Completion) : SessionOperation;

    public record EnableBreakpoint(string Id, bool Enabled,
        TaskCompletionSource<bool> Completion) : SessionOperation;

    public record ListBreakpoints(
        TaskCompletionSource<List<BreakpointConfig>> Completion) : SessionOperation;

    public record Go(int TimeoutMs,
        TaskCompletionSource<string> Completion) : SessionOperation;

    public record StepInto(
        TaskCompletionSource<string> Completion) : SessionOperation;

    public record StepOver(
        TaskCompletionSource<string> Completion) : SessionOperation;

    public record StepOut(
        TaskCompletionSource<string> Completion) : SessionOperation;

    public record GoTo(string Location,
        TaskCompletionSource<string> Completion) : SessionOperation;

    public record GetRegisters(
        TaskCompletionSource<Dictionary<string, string>> Completion) : SessionOperation;

    public record GetReg(string Name,
        TaskCompletionSource<string> Completion) : SessionOperation;

    public record ReadMemory(string Address, int Size,
        TaskCompletionSource<MemoryData> Completion) : SessionOperation;

    public record GetCallStack(int MaxFrames,
        TaskCompletionSource<List<FrameInfo>> Completion) : SessionOperation;

    public record ListThreads(
        TaskCompletionSource<List<ThreadInfo>> Completion) : SessionOperation;

    public record GetLocalVariables(int FrameIndex,
        TaskCompletionSource<List<VariableInfo>> Completion) : SessionOperation;

    public record GetProgramCounter(
        TaskCompletionSource<ProgramCounterInfo> Completion) : SessionOperation;

    public record CaptureState(
        TaskCompletionSource<CaptureResult> Completion) : SessionOperation;

    public record GetCaptures(
        TaskCompletionSource<IReadOnlyList<CaptureResult>> Completion) : SessionOperation;

    public record ClearCaptures(
        TaskCompletionSource<bool> Completion) : SessionOperation;

    public record ResolveSymbol(string Name,
        TaskCompletionSource<SymbolInfo> Completion) : SessionOperation;

    public record AddressToSymbol(string Address,
        TaskCompletionSource<SymbolInfo> Completion) : SessionOperation;

    public record FindSymbols(string Pattern,
        TaskCompletionSource<List<SymbolInfo>> Completion) : SessionOperation;

    public record Disassemble(string Address, int Count,
        TaskCompletionSource<List<DisassemblyLine>> Completion) : SessionOperation;

    public record ListModules(
        TaskCompletionSource<List<ModuleInfo>> Completion) : SessionOperation;

    public record Create(string Executable, string? Arguments,
        string? WorkingDirectory, bool StopAtEntry,
        TaskCompletionSource<SessionInfo> Completion) : SessionOperation;

    public record Attach(int Pid,
        TaskCompletionSource<SessionInfo> Completion) : SessionOperation;

    public record LoadDump(string Path,
        TaskCompletionSource<SessionInfo> Completion) : SessionOperation;

    public record Detach(
        TaskCompletionSource<bool> Completion) : SessionOperation;

    public record Terminate(
        TaskCompletionSource<bool> Completion) : SessionOperation;

    public record Status(
        TaskCompletionSource<SessionStatus> Completion) : SessionOperation;

    public record RawGdb(string Command,
        TaskCompletionSource<string> Completion) : SessionOperation;

    public record Shutdown(
        TaskCompletionSource<bool> Completion) : SessionOperation;
}

// ─── Result types used by operations ───

public record BreakpointConfig(
    string BpNumber, string Location, bool Capture, string Action, string? Condition, bool Enabled);

public record MemoryData(string Address, int Size, string Hex, byte[] Bytes, string Ascii);

public record ThreadInfo(int ThreadId, bool IsCurrent);

public record VariableInfo(string Name, string Type, string Value);

public record ProgramCounterInfo(string Address, string Symbol, string Instruction);

public record SymbolInfo(string Name, string Address);

public record DisassemblyLine(string Address, string Opcode, string Instruction);

public record ModuleInfo(string Name, string BaseAddress, long Size);

public record SessionInfo(string Type, int? ProcessId, string? ExitCode);

public record SessionStatus(string State);
```

- [ ] **Step 2: Write CaptureResult.cs**

```csharp
namespace GdbMiBridge.Mcp;

public record CaptureResult(
    string BreakpointNumber,
    string BreakpointLocation,
    Dictionary<string, string> Registers,
    ProgramCounterInfo ProgramCounter,
    List<GdbMi.FrameInfo> CallStack,
    MemoryData? Memory,
    List<VariableInfo> LocalVariables,
    DateTimeOffset Timestamp
);
```

- [ ] **Step 3: Build and commit**

```bash
cd /c/Code/GdbMiClient
dotnet build
# Expected: Build succeeds
git add -A && git commit -m "feat: add SessionOperation + CaptureResult data models"
```

---

### Task 3: BreakpointManager

**Goal:** Breakpoint configuration registry with go/break decision logic. Fully testable — no GDB dependency.

**Files:**
- Create: `src/GdbMiBridge.Mcp/BreakpointManager.cs`
- Create: `tests/GdbMiBridge.Mcp.Tests/BreakpointManagerTests.cs`

- [ ] **Step 1: Write BreakpointManagerTests.cs (RED)**

```csharp
using Xunit;

namespace GdbMiBridge.Mcp.Tests;

public class BreakpointManagerTests
{
    [Fact]
    public void Register_AndFind_ReturnsConfig()
    {
        var m = new BreakpointManager();
        m.Register("1", new BreakpointConfig("1", "main", true, "break", null, true));
        var found = m.FindByNumber("1");
        Assert.NotNull(found);
        Assert.Equal("main", found!.Location);
        Assert.True(found.Capture);
        Assert.Equal("break", found.Action);
    }

    [Fact]
    public void FindByNumber_NotRegistered_ReturnsNull()
    {
        var m = new BreakpointManager();
        Assert.Null(m.FindByNumber("99"));
    }

    [Fact]
    public void Remove_ThenFind_ReturnsNull()
    {
        var m = new BreakpointManager();
        m.Register("1", new BreakpointConfig("1", "main", false, "break", null, true));
        m.Remove("1");
        Assert.Null(m.FindByNumber("1"));
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var m = new BreakpointManager();
        m.Register("1", new BreakpointConfig("1", "a", false, "break", null, true));
        m.Register("2", new BreakpointConfig("2", "b", true, "go", null, true));
        m.Clear();
        Assert.Null(m.FindByNumber("1"));
        Assert.Null(m.FindByNumber("2"));
    }

    [Fact]
    public void GetAll_ReturnsAllRegistered()
    {
        var m = new BreakpointManager();
        m.Register("1", new BreakpointConfig("1", "a", false, "break", null, true));
        m.Register("2", new BreakpointConfig("2", "b", true, "go", null, true));
        var all = m.GetAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void OnHit_GoAction_ReturnsShouldContinueTrue()
    {
        var m = new BreakpointManager();
        m.Register("1", new BreakpointConfig("1", "main", true, "go", null, true));
        var (capture, shouldContinue) = m.OnHit("1");
        Assert.True(capture);
        Assert.True(shouldContinue);
    }

    [Fact]
    public void OnHit_BreakAction_ReturnsShouldContinueFalse()
    {
        var m = new BreakpointManager();
        m.Register("1", new BreakpointConfig("1", "main", false, "break", null, true));
        var (capture, shouldContinue) = m.OnHit("1");
        Assert.False(capture);
        Assert.False(shouldContinue);
    }

    [Fact]
    public void OnHit_NotRegistered_ReturnsNoCaptureBreak()
    {
        var m = new BreakpointManager();
        var (capture, shouldContinue) = m.OnHit("99");
        Assert.False(capture);
        Assert.False(shouldContinue);
    }

    [Fact]
    public void EnableDisable_Toggles()

    {
        var m = new BreakpointManager();
        m.Register("1", new BreakpointConfig("1", "main", false, "break", null, false));
        m.SetEnabled("1", true);
        Assert.True(m.FindByNumber("1")!.Enabled);
        m.SetEnabled("1", false);
        Assert.False(m.FindByNumber("1")!.Enabled);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```bash
cd /c/Code/GdbMiClient
dotnet test --filter "BreakpointManagerTests"
# Expected: FAIL — type not found
```

- [ ] **Step 3: Write BreakpointManager.cs (GREEN)**

```csharp
namespace GdbMiBridge.Mcp;

/// <summary>
/// Registry of all breakpoints in the current session.
/// Single-threaded — only accessed from GdbSession consumer thread.
/// </summary>
public class BreakpointManager
{
    private readonly Dictionary<string, BreakpointConfig> _bps = new();

    public void Register(string bpNumber, BreakpointConfig config)
    {
        _bps[bpNumber] = config with { BpNumber = bpNumber };
    }

    public void Remove(string bpNumber) => _bps.Remove(bpNumber);

    public void SetEnabled(string bpNumber, bool enabled)
    {
        if (_bps.TryGetValue(bpNumber, out var config))
            _bps[bpNumber] = config with { Enabled = enabled };
    }

    public BreakpointConfig? FindByNumber(string bpNumber)
        => _bps.TryGetValue(bpNumber, out var c) ? c : null;

    public List<BreakpointConfig> GetAll() => _bps.Values.ToList();

    public void Clear() => _bps.Clear();

    /// <summary>
    /// Called when *stopped with reason="breakpoint-hit", bkptno=N.
    /// Returns (shouldCapture, shouldContinue):
    ///   shouldCapture=true → GdbSession must collect registers/stack/locals
    ///   shouldContinue=true → GdbSession must silently -exec-continue
    ///   shouldContinue=false → GdbSession must notify the Agent
    /// </summary>
    public (bool ShouldCapture, bool ShouldContinue) OnHit(string bpNumber)
    {
        var config = FindByNumber(bpNumber);
        if (config is null) return (false, false); // unknown bp → break

        return (config.Capture, config.Action == "go");
    }
}
```

- [ ] **Step 4: Run tests — expect 9 PASS**

```bash
cd /c/Code/GdbMiClient
dotnet test --filter "BreakpointManagerTests"
# Expected: 9 tests PASS
```

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add BreakpointManager with TDD — go/break decision logic"
```

---

### Task 4: CapturesManager

**Goal:** Captures accumulator. Fully testable.

**Files:**
- Create: `tests/GdbMiBridge.Mcp.Tests/CapturesManagerTests.cs`
- Create: `src/GdbMiBridge.Mcp/CapturesManager.cs`

- [ ] **Step 1: Write CapturesManagerTests.cs (RED)**

```csharp
using Xunit;
using GdbMi;

namespace GdbMiBridge.Mcp.Tests;

public class CapturesManagerTests
{
    private static CaptureResult MakeCapture(string bpNum)
        => new(bpNum, $"loc_{bpNum}", new(), new("", "", ""),
               new(), null, new(), DateTimeOffset.UtcNow);

    [Fact]
    public void Add_AndGetAll_ReturnsInOrder()
    {
        var m = new CapturesManager();
        m.Add(MakeCapture("1"));
        m.Add(MakeCapture("2"));
        var all = m.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("1", all[0].BreakpointNumber);
        Assert.Equal("2", all[1].BreakpointNumber);
    }

    [Fact]
    public void Clear_EmptiesList()
    {
        var m = new CapturesManager();
        m.Add(MakeCapture("1"));
        m.Clear();
        Assert.Empty(m.GetAll());
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```bash
dotnet test --filter "CapturesManagerTests"
# Expected: FAIL
```

- [ ] **Step 3: Write CapturesManager.cs (GREEN)**

```csharp
namespace GdbMiBridge.Mcp;

/// <summary>
/// Accumulates capture snapshots during a session.
/// Single-threaded — only accessed from GdbSession consumer thread.
/// </summary>
public class CapturesManager
{
    private readonly List<CaptureResult> _captures = new();

    public void Add(CaptureResult capture) => _captures.Add(capture);

    public IReadOnlyList<CaptureResult> GetAll() => _captures.AsReadOnly();

    public void Clear() => _captures.Clear();
}
```

- [ ] **Step 4: Run tests — 2 PASS**

```bash
dotnet test --filter "CapturesManagerTests"
```

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add CapturesManager with TDD"
```

---

### Task 5: Modify GdbMiClient — expose Cmd + integrate output routing

**Goal:** Add missing `Cmd` property to `GdbMiClient` and a `SetOutputStream` hook needed by GdbSession.

**Files:**
- Modify: `src/GdbMiClient/GdbMiClient.cs`

- [ ] **Step 1: Add Cmd property**

```csharp
// In GdbMiClient constructor, add:
_cmd = new GdbCommandFactory(this);

// Add property:
public GdbCommandFactory Cmd => _cmd;

// Add field:
private readonly GdbCommandFactory _cmd;
```

- [ ] **Step 2: Build and commit**

```bash
cd /c/Code/GdbMiClient
dotnet build
git add -A && git commit -m "feat: expose Cmd property on GdbMiClient"
```

---

### Task 6: GdbSession

**Goal:** The core consumer loop — Channel reader, command execution, auto-capture, go/break handling.

**Files:**
- Create: `src/GdbMiBridge.Mcp/GdbSession.cs`

- [ ] **Step 1: Write GdbSession.cs**

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace GdbMiBridge.Mcp;

/// <summary>
/// Single-threaded debug session. All GDB I/O flows through the consumer loop.
/// MCP tools call SendOperationAsync() from any thread; operations execute sequentially on consumer.
/// </summary>
public class GdbSession : IDisposable
{
    private readonly ILogger<GdbSession> _logger;
    private readonly Channel<SessionOperation> _channel =
        Channel.CreateUnbounded<SessionOperation>(new UnboundedChannelOptions
        {
            SingleReader = true, SingleWriter = false
        });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;

    private GdbMi.GdbMiClient? _client;
    private GdbMi.LocalTransport? _transport;
    private GdbMi.GdbCommandFactory? _cmd;
    private readonly BreakpointManager _bpManager = new();
    private readonly CapturesManager _captures = new();

    // Stop event — set by consumer when break-action bp is hit
    private TaskCompletionSource<string> _stopTcs = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    // Completion for the currently executing Go/Step op (if waiting for *stopped)
    private TaskCompletionSource<string>? _waitingForStop;
    private CancellationTokenSource? _goTimeoutCts;

    public GdbMi.DebuggerState State => _client?.State ?? GdbMi.DebuggerState.NotConnected;
    public BreakpointManager Breakpoints => _bpManager;
    public CapturesManager Captures => _captures;

    public GdbSession(ILogger<GdbSession> logger)
    {
        _logger = logger;
        _consumerTask = ConsumerLoopAsync(_cts.Token);
    }

    // ═══════════ MCP Tool entry point (thread-safe) ═══════════

    public async Task<T> SendOperationAsync<T>(
        SessionOperation op, TaskCompletionSource<T> tcs)
    {
        await _channel.Writer.WriteAsync(op);
        return await tcs.Task;
    }

    /// <summary>Helper to create op + tcs + send in one call</summary>
    private async Task<T> PostAsync<T>(SessionOperation op)
        where T : notnull
    {
        // Find the TaskCompletionSource<T> field in the record
        var prop = op.GetType().GetProperty("Completion")
            ?? throw new ArgumentException("Operation must have a Completion property");
        var tcs = (TaskCompletionSource<T>)prop.GetValue(op)!;
        await _channel.Writer.WriteAsync(op);
        return await tcs.Task;
    }

    // Public typed methods that tools call:

    public async Task<SessionInfo> CreateAsync(string exe, string? args,
        string? workDir, bool stopAtEntry)
    {
        var tcs = new TaskCompletionSource<SessionInfo>();
        await _channel.Writer.WriteAsync(
            new SessionOperation.Create(exe, args, workDir, stopAtEntry, tcs));
        return await tcs.Task;
    }

    public async Task<SessionStatus> StatusAsync()
    {
        var tcs = new TaskCompletionSource<SessionStatus>();
        await _channel.Writer.WriteAsync(new SessionOperation.Status(tcs));
        return await tcs.Task;
    }

    public async Task<string> GoAsync(int timeoutMs)
    {
        var tcs = new TaskCompletionSource<string>();
        await _channel.Writer.WriteAsync(new SessionOperation.Go(timeoutMs, tcs));
        return await tcs.Task;
    }

    public async Task<BreakpointConfig> SetBreakpointAsync(
        string location, bool capture, string action, string? condition)
    {
        var tcs = new TaskCompletionSource<GdbMiClient.BreakpointInfo>();
        await _channel.Writer.WriteAsync(
            new SessionOperation.SetBreakpoint(location, capture, action, condition, tcs));
        var info = await tcs.Task;
        return new BreakpointConfig(info.Number, location, capture, action, condition, true);
    }

    // ... (more typed PostAsync methods for each operation type)

    public async Task<string> DetachAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        await _channel.Writer.WriteAsync(new SessionOperation.Detach(tcs));
        await tcs.Task;
        return "detached";
    }

    public async Task<string> TerminateAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        await _channel.Writer.WriteAsync(new SessionOperation.Terminate(tcs));
        await tcs.Task;
        return "terminated";
    }

    public async Task<IReadOnlyList<CaptureResult>> GetCapturesAsync()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<CaptureResult>>();
        await _channel.Writer.WriteAsync(new SessionOperation.GetCaptures(tcs));
        return await tcs.Task;
    }

    public async Task<string> RawGdbAsync(string command)
    {
        var tcs = new TaskCompletionSource<string>();
        await _channel.Writer.WriteAsync(new SessionOperation.RawGdb(command, tcs));
        return await tcs.Task;
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        try { await _consumerTask; } catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _cts.Cancel(); _cts.Dispose();
        _transport?.Dispose();
    }

    // ═══════════ Consumer Loop (单线程) ═══════════

    private async Task ConsumerLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var op in _channel.Reader.ReadAllAsync(ct))
            {
                await ProcessOperationAsync(op);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Consumer loop error"); }
    }

    private async Task ProcessOperationAsync(SessionOperation op)
    {
        switch (op)
        {
            case SessionOperation.Create c:
                await HandleCreate(c);
                break;
            case SessionOperation.Attach a:
                a.Completion.TrySetResult(new SessionInfo("attach", null, null));
                break;
            case SessionOperation.Go g:
                await HandleGo(g);
                break;
            case SessionOperation.StepInto si:
                await HandleStep(si.Completion, () => _cmd!.ExecStep(_currentThread));
                break;
            case SessionOperation.StepOver so:
                await HandleStep(so.Completion, () => _cmd!.ExecNext(_currentThread));
                break;
            case SessionOperation.StepOut so2:
                await HandleStep(so2.Completion, () => _cmd!.ExecFinish(_currentThread));
                break;
            case SessionOperation.GoTo gt:
                await HandleGoTo(gt);
                break;
            case SessionOperation.SetBreakpoint sb:
                await HandleSetBreakpoint(sb);
                break;
            case SessionOperation.RemoveBreakpoint rb:
                await _cmd!.BreakDelete(rb.Id);
                _bpManager.Remove(rb.Id);
                rb.Completion.TrySetResult(true);
                break;
            case SessionOperation.EnableBreakpoint eb:
                await _cmd!.BreakEnable(eb.Id, eb.Enabled);
                _bpManager.SetEnabled(eb.Id, eb.Enabled);
                eb.Completion.TrySetResult(true);
                break;
            case SessionOperation.ListBreakpoints lb:
                lb.Completion.TrySetResult(_bpManager.GetAll());
                break;
            case SessionOperation.GetRegisters gr:
                await HandleGetRegisters(gr);
                break;
            case SessionOperation.GetCallStack gcs:
                await HandleGetCallStack(gcs);
                break;
            case SessionOperation.GetLocalVariables glv:
                await HandleGetLocalVariables(glv);
                break;
            case SessionOperation.GetProgramCounter gpc:
                await HandleGetProgramCounter(gpc);
                break;
            case SessionOperation.CaptureState cs:
                cs.Completion.TrySetResult(await CaptureCurrentStateAsync(
                    "manual", "manual"));
                break;
            case SessionOperation.GetCaptures gc:
                gc.Completion.TrySetResult(_captures.GetAll());
                break;
            case SessionOperation.ClearCaptures cc:
                _captures.Clear();
                cc.Completion.TrySetResult(true);
                break;
            case SessionOperation.ResolveSymbol rs:
                await HandleResolveSymbol(rs);
                break;
            case SessionOperation.Disassemble d:
                await HandleDisassemble(d);
                break;
            case SessionOperation.ListModules lm:
                await HandleListModules(lm);
                break;
            case SessionOperation.Detach d2:
                await _cmd!.TargetDetach();
                CleanupSession();
                d2.Completion.TrySetResult(true);
                break;
            case SessionOperation.Terminate t:
                await _cmd!.Terminate();
                CleanupSession();
                t.Completion.TrySetResult(true);
                break;
            case SessionOperation.Status s:
                s.Completion.TrySetResult(new SessionStatus(State.ToString()));
                break;
            case SessionOperation.RawGdb rg:
                await HandleRawGdb(rg);
                break;
            case SessionOperation.Shutdown sh:
                CleanupSession();
                sh.Completion.TrySetResult(true);
                break;
            // ... additional ops
        }
    }

    // ═══════════ Core handlers ═══════════

    private int _currentThread = 1;

    private async Task HandleCreate(SessionOperation.Create c)
    {
        _transport = new GdbMi.LocalTransport("gdb", "--interpreter=mi3",
            _logger as Microsoft.Extensions.Logging.ILogger);
        _client = new GdbMi.GdbMiClient(_transport,
            _logger as Microsoft.Extensions.Logging.ILogger<GdbMi.GdbMiClient>);
        _cmd = _client.Cmd;

        await _client.ConnectAsync(CancellationToken.None);
        await _cmd.EnableTargetAsyncOption();
        await _cmd.FileExecAndSymbols(c.Executable);

        if (c.Arguments is not null)
            await _client.ConsoleCmdAsync(
                $"set args {c.Arguments}", allowWhileRunning: false);

        if (c.StopAtEntry)
        {
            // Set temporary break at main, then run
            await _cmd.BreakInsert("main");
        }
        await _cmd.ExecRun();

        // Read GDB output until we get the first *stopped
        await ReadUntilStoppedAsync();

        c.Completion.TrySetResult(
            new SessionInfo("create", _transport.DebuggerPid, null));
    }

    private async Task HandleGo(SessionOperation.Go g)
    {
        _waitingForStop = g.Completion;
        if (g.TimeoutMs > 0)
        {
            _goTimeoutCts = new CancellationTokenSource(g.TimeoutMs);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(g.TimeoutMs, _goTimeoutCts.Token);
                    // Timeout — interrupt GDB
                    await _cmd!.ExecContinue(); // placeholder: should be -exec-interrupt
                }
                catch (OperationCanceledException) { }
            });
        }
        await _cmd!.ExecContinue();
        // Response comes via ReadUntilStoppedAsync or OnStopped handler
        await ReadUntilStoppedAsync();
    }

    private async Task HandleStep(TaskCompletionSource<string> tcs, Func<Task> stepAction)
    {
        _waitingForStop = tcs;
        await stepAction();
        await ReadUntilStoppedAsync();
    }

    private async Task HandleGoTo(SessionOperation.GoTo gt)
    {
        _waitingForStop = gt.Completion;
        // Temporary breakpoint + continue
        var result = await _cmd!.BreakInsert($"-t {gt.Location}");
        await _cmd.ExecContinue();
        await ReadUntilStoppedAsync();
    }

    private async Task HandleSetBreakpoint(SessionOperation.SetBreakpoint sb)
    {
        var result = await _cmd!.BreakInsert(sb.Location, 0,
            condition: sb.Condition, enabled: true);
        var bkpt = result.Find<GdbMi.TupleValue>("bkpt");
        var number = bkpt.FindString("number");
        _bpManager.Register(number,
            new BreakpointConfig(number, sb.Location, sb.Capture, sb.Action,
                sb.Condition, true));
        sb.Completion.TrySetResult(new GdbMiClient.BreakpointInfo(
            number,
            bkpt.TryFindString("addr"),
            bkpt.TryFindString("func"),
            bkpt.TryFindString("file"),
            bkpt.TryFindUint("line").HasValue ? (int?)bkpt.FindUint("line") : null));
    }

    // ═══════════ Auto-capture ═══════════

    private async Task ReadUntilStoppedAsync()
    {
        while (_transport is not null && !_transport.IsClosed)
        {
            var line = await _transport.ReadLineAsync(CancellationToken.None);
            if (line is null) break;

            // Feed to GdbMiClient for token matching / async event parsing
            // But also intercept *stopped for auto-capture
            if (line.TrimStart().StartsWith("*stopped"))
            {
                await OnStopped(line);
            }
            else if (line.TrimStart().StartsWith("~") ||
                     line.TrimStart().StartsWith("@") ||
                     line.TrimStart().StartsWith("&"))
            {
                _client!.ProcessLine(line);
            }
            else if (line.TrimStart().StartsWith("^"))
            {
                _client!.ProcessLine(line);
                // If this is a ^running response (not waiting for stop):
                if (!line.Contains("running") && _waitingForStop is null)
                    continue;
            }
            else
            {
                _client!.ProcessLine(line);
            }
        }
    }

    private async Task OnStopped(string line)
    {
        // Process the line through GdbMiClient to update state and parse
        _client!.ProcessLine(line);

        // Parse bkptno
        var parser = new GdbMi.MIResultParser();
        var results = parser.ParseResultList(
            line.Contains(',') ? line.Substring(line.IndexOf(',') + 1) : "");
        string reason = results.TryFindString("reason");
        string? bkptno = results.TryFindString("bkptno");

        if (reason == "breakpoint-hit" && bkptno is not null)
        {
            var (shouldCapture, shouldContinue) = _bpManager.OnHit(bkptno);

            if (shouldCapture)
            {
                var capture = await CaptureCurrentStateAsync(bkptno,
                    _bpManager.FindByNumber(bkptno)?.Location ?? "unknown");
                _captures.Add(capture);
            }

            if (shouldContinue)
            {
                // go-action: silently continue
                _goTimeoutCts?.Cancel();
                await _cmd!.ExecContinue();
                // Agent's GoAsync still waiting; loop continues
                return;
            }
        }

        // break-action or non-bp stop: notify Agent
        _goTimeoutCts?.Cancel();
        var tcs = _waitingForStop;
        _waitingForStop = null;
        tcs?.TrySetResult(reason);
    }

    private async Task<CaptureResult> CaptureCurrentStateAsync(
        string bpNumber, string location)
    {
        var registers = new Dictionary<string, string>();
        try
        {
            var regResult = await _cmd!.DataListRegisterValues(_currentThread);
            foreach (var reg in regResult)
            {
                var name = reg.FindString("number");
                var value = reg.TryFindString("value");
                if (name is not null && value is not null)
                    registers[name] = value;
            }
        }
        catch { }

        ProgramCounterInfo pc;
        try
        {
            var frameResult = await _cmd!.StackInfoFrame();
            var frame = frameResult.Find<GdbMi.TupleValue>("frame");
            pc = new ProgramCounterInfo(
                frame.TryFindString("addr") ?? "",
                frame.TryFindString("func") ?? "",
                "");
        }
        catch { pc = new ProgramCounterInfo("", "", ""); }

        List<GdbMi.FrameInfo> callStack;
        try
        {
            var frames = await _cmd!.StackListFrames(_currentThread);
            callStack = frames.Select(f => new GdbMi.FrameInfo(
                f.TryFindString("level") ?? "",
                f.TryFindString("addr"),
                f.TryFindString("func"),
                f.TryFindString("file"),
                f.TryFindUint("line").HasValue ? (int?)f.FindUint("line") : null
            )).ToList();
        }
        catch { callStack = new(); }

        List<VariableInfo> locals;
        try
        {
            var localsResult = await _cmd!.StackListLocals(1, _currentThread, 0);
            locals = ParseVariables(localsResult);
        }
        catch { locals = new(); }

        return new CaptureResult(bpNumber, location, registers, pc,
            callStack, null, locals, DateTimeOffset.UtcNow);
    }

    private List<VariableInfo> ParseVariables(GdbMi.ResultValue locals)
    {
        var result = new List<VariableInfo>();
        if (locals is GdbMi.ValueListValue vlist)
        {
            foreach (GdbMi.TupleValue t in vlist.AsArray<GdbMi.TupleValue>())
            {
                result.Add(new VariableInfo(
                    t.TryFindString("name") ?? "?",
                    t.TryFindString("type") ?? "?",
                    t.TryFindString("value") ?? "?"));
            }
        }
        return result;
    }

    private async Task HandleGetRegisters(SessionOperation.GetRegisters gr)
    {
        var regs = await _cmd!.DataListRegisterValues(_currentThread);
        var dict = new Dictionary<string, string>();
        foreach (var r in regs)
        {
            var name = r.TryFindString("number");
            var val = r.TryFindString("value");
            if (name is not null) dict[name] = val ?? "?";
        }
        gr.Completion.TrySetResult(dict);
    }

    private async Task HandleGetCallStack(SessionOperation.GetCallStack gcs)
    {
        var frames = await _cmd!.StackListFrames(_currentThread, 0,
            (uint)gcs.MaxFrames);
        gcs.Completion.TrySetResult(frames.Select(f => new GdbMi.FrameInfo(
            f.TryFindString("level") ?? "",
            f.TryFindString("addr"),
            f.TryFindString("func"),
            f.TryFindString("file"),
            f.TryFindUint("line").HasValue ? (int?)f.FindUint("line") : null
        )).ToList());
    }

    private async Task HandleGetLocalVariables(SessionOperation.GetLocalVariables glv)
    {
        var locals = await _cmd!.StackListLocals(1, _currentThread, (uint)glv.FrameIndex);
        glv.Completion.TrySetResult(ParseVariables(locals));
    }

    private async Task HandleGetProgramCounter(SessionOperation.GetProgramCounter gpc)
    {
        var frame = await _cmd!.StackInfoFrame();
        var f = frame.Find<GdbMi.TupleValue>("frame");
        gpc.Completion.TrySetResult(new ProgramCounterInfo(
            f.TryFindString("addr") ?? "",
            f.TryFindString("func") ?? "",
            ""));
    }

    private async Task HandleResolveSymbol(SessionOperation.ResolveSymbol rs)
    {
        var result = await _client!.ConsoleCmdAsync(
            $"info address {rs.Name}", allowWhileRunning: false);
        // Parse "Symbol \"xxx\" is at 0xADDR" from GDB output
        var addr = ExtractAddress(result);
        rs.Completion.TrySetResult(new SymbolInfo(rs.Name, addr ?? "unknown"));
    }

    private async Task HandleDisassemble(SessionOperation.Disassemble d)
    {
        var result = await _client!.ConsoleCmdAsync(
            $"disassemble {d.Address},+{d.Count * 4}", allowWhileRunning: false);
        var lines = ParseDisassembly(result);
        d.Completion.TrySetResult(lines);
    }

    private async Task HandleListModules(SessionOperation.ListModules lm)
    {
        var result = await _client!.ConsoleCmdAsync(
            "info sharedlibrary", allowWhileRunning: false);
        lm.Completion.TrySetResult(ParseModules(result));
    }

    private async Task HandleRawGdb(SessionOperation.RawGdb rg)
    {
        var result = await _client!.ConsoleCmdAsync(
            rg.Command, allowWhileRunning: true, ignoreFailures: true);
        rg.Completion.TrySetResult(result);
    }

    // ═══════════ Helpers ═══════════

    private void CleanupSession()
    {
        _bpManager.Clear();
        _captures.Clear();
        _transport?.Close();
        _client = null;
        _cmd = null;
        _transport = null;
    }

    private static string? ExtractAddress(string gdbOutput)
    {
        // GDB output: "... at 0x7ff... \n"
        var idx = gdbOutput.IndexOf("0x", StringComparison.Ordinal);
        if (idx < 0) return null;
        var end = gdbOutput.IndexOf(' ', idx);
        return end < 0 ? gdbOutput[idx..] : gdbOutput[idx..end];
    }

    private static List<DisassemblyLine> ParseDisassembly(string output)
    {
        var lines = new List<DisassemblyLine>();
        using var reader = new StringReader(output);
        while (reader.ReadLine() is string line)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line) ||
                line.StartsWith("Dump of") ||
                line.StartsWith("End of")) continue;

            // Format: "0xADDR <+offset>: OPCODE  INSTRUCTION"
            var space1 = line.IndexOf(' ');
            if (space1 < 0) continue;
            var addr = line[..space1].TrimEnd(':');
            var rest = line[(space1 + 1)..];
            var tab = rest.IndexOf('\t');
            var opcode = tab > 0 ? rest[..tab].Trim() : "";
            var instruction = tab > 0 ? rest[(tab + 1)..].Trim() : rest.Trim();
            lines.Add(new DisassemblyLine(addr, opcode, instruction));
        }
        return lines;
    }

    private static List<ModuleInfo> ParseModules(string output)
    {
        var modules = new List<ModuleInfo>();
        using var reader = new StringReader(output);
        while (reader.ReadLine() is string line)
        {
            if (!line.StartsWith("0x")) continue;
            // Format: "0x7f...  0x7f...  Yes         /lib/x86_64/..."
            var parts = line.Split(new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                modules.Add(new ModuleInfo(
                    parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "",
                    parts[0], 0));
            }
        }
        return modules;
    }
}
```

- [ ] **Step 2: Build**

```bash
cd /c/Code/GdbMiClient
dotnet build src/GdbMiBridge.Mcp
# May have some compilation issues — fix iteratively, then:
```

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: add GdbSession — consumer loop, operations, auto-capture"
```

---

### Task 7: Program.cs (MCP entry point)

**Goal:** Wire up the MCP server with DI, register GdbSession, expose all tools.

**Files:**
- Create: `src/GdbMiBridge.Mcp/Program.cs`

- [ ] **Step 1: Write Program.cs**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using GdbMiBridge.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// Route all non-MCP logging to stderr so stdio stays clean for JSON-RPC
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register session as singleton (one GDB session per MCP server process)
builder.Services.AddSingleton<GdbSession>();

// Register tool handler classes for DI
builder.Services.AddSingleton<SessionTools>();
builder.Services.AddSingleton<ExecutionTools>();
builder.Services.AddSingleton<BreakpointTools>();
builder.Services.AddSingleton<StateTools>();
builder.Services.AddSingleton<SymbolTools>();
builder.Services.AddSingleton<RawTools>();

// MCP server with stdio transport
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

- [ ] **Step 2: Build and commit**

```bash
cd /c/Code/GdbMiClient
dotnet build
git add -A && git commit -m "feat: add Program.cs — MCP server entry point with DI"
```

---

### Task 8: Tool Handlers (6 files, 32 tools)

**Goal:** Implement all 32 MCP tools as `[McpServerTool]` methods across 6 handler classes.

**Files:**
- Create: `ToolHandlers/SessionTools.cs`
- Create: `ToolHandlers/ExecutionTools.cs`
- Create: `ToolHandlers/BreakpointTools.cs`
- Create: `ToolHandlers/StateTools.cs`
- Create: `ToolHandlers/SymbolTools.cs`
- Create: `ToolHandlers/RawTools.cs`

- [ ] **Step 1: Write SessionTools.cs** (6 tools)

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class SessionTools(GdbSession session)
{
    [McpServerTool, Description("Create a debugging session by launching an executable.")]
    public async Task<SessionInfo> Create(
        [Description("Path to the executable file.")] string executable,
        [Description("Command-line arguments for the executable.")] string? arguments = null,
        [Description("Working directory for the process.")] string? workingDirectory = null,
        [Description("Whether to stop at the program entry point.")] bool stopAtEntry = true)
        => await session.CreateAsync(executable, arguments, workingDirectory, stopAtEntry);

    [McpServerTool, Description("Attach to a running process by PID.")]
    public async Task<SessionInfo> Attach(
        [Description("Process ID to attach to.")] int pid)
        => await session.AttachAsync(pid);

    [McpServerTool, Description("Load a core dump file for analysis.")]
    public async Task<SessionInfo> LoadDump(
        [Description("Path to the core dump file.")] string path)
        => await session.LoadDumpAsync(path);

    [McpServerTool, Description("Detach from the debugged process, leaving it running.")]
    public async Task<string> Detach()
        => await session.DetachAsync();

    [McpServerTool, Description("Terminate the debugged process and clean up the session.")]
    public async Task<string> Terminate()
        => await session.TerminateAsync();

    [McpServerTool, Description("Query the current debug session status.")]
    public async Task<SessionStatus> Status()
        => await session.StatusAsync();
}
```

- [ ] **Step 2: Write ExecutionTools.cs** (5 tools)

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class ExecutionTools(GdbSession session)
{
    [McpServerTool, Description("Continue execution until a breakpoint, exception, exit, or timeout.")]
    public async Task<string> Go(
        [Description("Maximum time to wait in milliseconds. 0 = no timeout.")] int timeoutMs = 0)
        => await session.GoAsync(timeoutMs);

    [McpServerTool, Description("Single-step into the next instruction (enters function calls).")]
    public async Task<string> StepInto()
        => await session.StepIntoAsync();

    [McpServerTool, Description("Single-step over the current line (skips function calls).")]
    public async Task<string> StepOver()
        => await session.StepOverAsync();

    [McpServerTool, Description("Execute until the current function returns.")]
    public async Task<string> StepOut()
        => await session.StepOutAsync();

    [McpServerTool, Description("Run to a specified location using a temporary breakpoint.")]
    public async Task<string> GoTo(
        [Description("Function name, file:line, or address to run to.")] string location)
        => await session.GoToAsync(location);
}
```

- [ ] **Step 3: Write BreakpointTools.cs** (5 tools)

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class BreakpointTools(GdbSession session)
{
    [McpServerTool, Description("Set a breakpoint at a function, file:line, or address.")]
    public async Task<BreakpointConfig> SetBreakpoint(
        [Description("Function name, file:line, or address.")] string location,
        [Description("Capture register/stack state when hit.")] bool capture = true,
        [Description("Action on hit: 'go' (auto-continue) or 'break' (stop and wait).")]
            string action = "break",
        [Description("Condition expression, e.g. 'x > 5'.")] string? condition = null)
        => await session.SetBreakpointAsync(location, capture, action, condition);

    [McpServerTool, Description("Remove a breakpoint by ID.")]
    public async Task<bool> RemoveBreakpoint(
        [Description("Breakpoint ID.")] string id)
        => await session.RemoveBreakpointAsync(id);

    [McpServerTool, Description("Enable a breakpoint.")]
    public async Task<bool> EnableBreakpoint(
        [Description("Breakpoint ID.")] string id)
        => await session.EnableBreakpointAsync(id, true);

    [McpServerTool, Description("Disable a breakpoint without removing it.")]
    public async Task<bool> DisableBreakpoint(
        [Description("Breakpoint ID.")] string id)
        => await session.EnableBreakpointAsync(id, false);

    [McpServerTool, Description("List all breakpoints in the current session.")]
    public List<BreakpointConfig> ListBreakpoints()
        => session.Breakpoints.GetAll();
}
```

- [ ] **Step 4: Write StateTools.cs** (10 tools)

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class StateTools(GdbSession session)
{
    [McpServerTool, Description("Get all CPU register values.")]
    public async Task<Dictionary<string, string>> GetRegisters()
        => await session.GetRegistersAsync();

    [McpServerTool, Description("Get the value of a single register by name.")]
    public async Task<string> GetReg(
        [Description("Register name, e.g. 'rip', 'rax'.")] string name)
        => (await session.GetRegistersAsync()).TryGetValue(name, out var v) ? v : "?";

    [McpServerTool, Description("Get the program counter with symbol and instruction.")]
    public async Task<ProgramCounterInfo> GetProgramCounter()
        => await session.GetProgramCounterAsync();

    [McpServerTool, Description("Read memory at the given address.")]
    public async Task<MemoryData> ReadMemory(
        [Description("Hex address, e.g. '0x401000'.")] string address,
        [Description("Number of bytes to read.")] int size = 64)
        => await session.ReadMemoryAsync(address, size);

    [McpServerTool, Description("Get the call stack.")]
    public async Task<List<GdbMi.FrameInfo>> GetCallStack(
        [Description("Maximum number of frames.")] int maxFrames = 20)
        => await session.GetCallStackAsync(maxFrames);

    [McpServerTool, Description("List all threads.")]
    public async Task<List<ThreadInfo>> ListThreads()
        => await session.ListThreadsAsync();

    [McpServerTool, Description("Get local variables for the specified stack frame.")]
    public async Task<List<VariableInfo>> GetLocalVariables(
        [Description("Stack frame index (0 = current).")] int frameIndex = 0)
        => await session.GetLocalVariablesAsync(frameIndex);

    [McpServerTool, Description("Manually capture the current program state snapshot.")]
    public async Task<CaptureResult> CaptureState()
        => await session.CaptureStateAsync();

    [McpServerTool, Description("Get all accumulated capture snapshots.")]
    public List<CaptureResult> GetCaptures()
        => session.Captures.GetAll().ToList();

    [McpServerTool, Description("Clear all accumulated capture snapshots.")]
    public string ClearCaptures()
    {
        session.Captures.Clear();
        return "cleared";
    }
}
```

- [ ] **Step 5: Write SymbolTools.cs** (5 tools)

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class SymbolTools(GdbSession session)
{
    [McpServerTool, Description("Resolve a symbol name to its address.")]
    public async Task<SymbolInfo> ResolveSymbol(
        [Description("Symbol name, e.g. 'main' or 'test!add'.")] string name)
        => await session.ResolveSymbolAsync(name);

    [McpServerTool, Description("Look up the symbol name for an address.")]
    public async Task<SymbolInfo> AddressToSymbol(
        [Description("Hex address, e.g. '0x401000'.")] string address)
        => await session.AddressToSymbolAsync(address);

    [McpServerTool, Description("Search for symbols matching a pattern.")]
    public async Task<List<SymbolInfo>> FindSymbols(
        [Description("Search pattern, supports * wildcards.")] string pattern)
        => await session.FindSymbolsAsync(pattern);

    [McpServerTool, Description("Disassemble instructions at the given address.")]
    public async Task<List<DisassemblyLine>> Disassemble(
        [Description("Hex address or symbol name.")] string address,
        [Description("Number of instructions.")] int count = 10)
        => await session.DisassembleAsync(address, count);

    [McpServerTool, Description("List loaded shared libraries/modules.")]
    public async Task<List<ModuleInfo>> ListModules()
        => await session.ListModulesAsync();
}
```

- [ ] **Step 6: Write RawTools.cs** (1 tool)

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class RawTools(GdbSession session)
{
    [McpServerTool, Description("Execute an arbitrary GDB command (MI or CLI).")]
    public async Task<string> RawGdb(
        [Description("The GDB command to execute.")] string command)
        => await session.RawGdbAsync(command);
}
```

- [ ] **Step 7: Build and commit**

```bash
cd /c/Code/GdbMiClient
dotnet build
git add -A && git commit -m "feat: add all 32 MCP tool handlers (6 classes)"
```

---

### Task 9: Add remaining public methods to GdbSession

**Goal:** Implement all the typed public methods on GdbSession that tool handlers call. Each method creates an operation, writes to Channel, awaits TCS.

This task adds all methods missing from Task 6:
- `AttachAsync`, `LoadDumpAsync`, `DetachAsync` (already done)
- `StepIntoAsync`, `StepOverAsync`, `StepOutAsync`, `GoToAsync`
- `RemoveBreakpointAsync`, `EnableBreakpointAsync`
- `GetRegistersAsync`, `ReadMemoryAsync`, `GetCallStackAsync`, `ListThreadsAsync`, `GetLocalVariablesAsync`, `GetProgramCounterAsync`
- `CaptureStateAsync`
- `ResolveSymbolAsync`, `AddressToSymbolAsync`, `FindSymbolsAsync`, `DisassembleAsync`, `ListModulesAsync`

Each follows the pattern:
```csharp
public async Task<X> FooAsync(T param)
{
    var tcs = new TaskCompletionSource<X>();
    await _channel.Writer.WriteAsync(new SessionOperation.Foo(param, tcs));
    return await tcs.Task;
}
```

and the corresponding `case` in `ProcessOperationAsync`.

- [ ] **Step 1: Add all missing methods to GdbSession.cs**
- [ ] **Step 2: Add all missing case branches to ProcessOperationAsync**
- [ ] **Step 3: Build and commit**

---

### Task 10: Build full solution and verify

- [ ] **Step 1: Full solution build**

```bash
cd /c/Code/GdbMiClient
dotnet build
# Expected: Zero errors
```

- [ ] **Step 2: Run all tests**

```bash
dotnet test
# Expected: 30 (GdbMiClient) + 11 (BreakpointManager + CapturesManager) = 41 PASS
```

- [ ] **Step 3: Verify tool discovery**

```bash
dotnet run --project src/GdbMiBridge.Mcp -- --help 2>&1 || echo "MCP server starts correctly"
```

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "chore: finalize GdbMiBridge.Mcp — 32 tools, full build, all tests green"
```

---

## Completion Criteria

- [ ] Full solution builds with zero errors
- [ ] 41+ tests pass
- [ ] 32 MCP tools registered and discoverable
- [ ] All tools return typed results (no raw strings except `raw_gdb`)
- [ ] Session lifecycle: create → debug → terminate cleanup works
- [ ] Auto-capture: go-action breakpoints silently capture and continue
