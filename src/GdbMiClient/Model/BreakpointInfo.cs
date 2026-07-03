namespace GdbMiClient;

public record BreakpointInfo(
    string Number,
    string? Address,
    string? FunctionName,
    string? File,
    int? Line
);
