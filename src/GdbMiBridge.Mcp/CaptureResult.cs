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
