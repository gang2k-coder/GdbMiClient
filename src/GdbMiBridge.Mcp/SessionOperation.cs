namespace GdbMiBridge.Mcp;

public abstract record SessionOperation
{
    // ── Session ──
    public record Create(string Executable, string? Arguments,
        string? WorkingDirectory, bool StopAtEntry,
        TaskCompletionSource<SessionInfo> Completion) : SessionOperation;

    public record Attach(int Pid,
        TaskCompletionSource<SessionInfo> Completion) : SessionOperation;

    public record LoadDump(string Path,
        TaskCompletionSource<SessionInfo> Completion) : SessionOperation;

    public record Detach(
        TaskCompletionSource<string> Completion) : SessionOperation;

    public record Terminate(
        TaskCompletionSource<string> Completion) : SessionOperation;

    public record Status(
        TaskCompletionSource<SessionStatus> Completion) : SessionOperation;

    // ── Execution ──
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

    // ── Breakpoints ──
    public record SetBreakpoint(string Location, bool Capture, string Action,
        string? Condition,
        TaskCompletionSource<BreakpointConfig> Completion) : SessionOperation;

    public record RemoveBreakpoint(string Id,
        TaskCompletionSource<bool> Completion) : SessionOperation;

    public record EnableBreakpoint(string Id, bool Enabled,
        TaskCompletionSource<bool> Completion) : SessionOperation;

    public record ListBreakpoints(
        TaskCompletionSource<List<BreakpointConfig>> Completion) : SessionOperation;

    // ── State ──
    public record GetRegisters(
        TaskCompletionSource<Dictionary<string, string>> Completion) : SessionOperation;

    public record ReadMemory(string Address, int Size,
        TaskCompletionSource<MemoryData> Completion) : SessionOperation;

    public record GetCallStack(int MaxFrames,
        TaskCompletionSource<List<GdbMi.FrameInfo>> Completion) : SessionOperation;

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

    // ── Symbol ──
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

    // ── Raw ──
    public record RawGdb(string Command,
        TaskCompletionSource<string> Completion) : SessionOperation;
}

// ═══════════ Result types ═══════════

public record BreakpointConfig(
    string BpNumber, string Location, bool Capture, string Action,
    string? Condition, bool Enabled);

public record MemoryData(string Address, int Size, string Hex, byte[] Bytes, string Ascii);

public record ThreadInfo(int ThreadId, bool IsCurrent);

public record VariableInfo(string Name, string Type, string Value);

public record ProgramCounterInfo(string Address, string Symbol, string Instruction);

public record SymbolInfo(string Name, string Address);

public record DisassemblyLine(string Address, string Opcode, string Instruction);

public record ModuleInfo(string Name, string BaseAddress, long Size);

public record SessionInfo(string Type, int? ProcessId, string? ExitCode);

public record SessionStatus(string State);
