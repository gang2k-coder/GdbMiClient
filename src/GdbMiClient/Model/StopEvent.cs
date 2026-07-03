namespace GdbMiClient;

public record StopEvent(
    string Reason,
    string? BreakpointNumber,
    string? SignalName,
    int? ThreadId,
    bool AllThreadsStopped,
    FrameInfo? Frame
);

public record FrameInfo(
    string Level,
    string? Address,
    string? FunctionName,
    string? File,
    int? Line
);
