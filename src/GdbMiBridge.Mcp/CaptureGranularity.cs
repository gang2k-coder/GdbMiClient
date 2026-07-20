namespace GdbMiBridge.Mcp;

public enum RegisterPreset
{
    None,
    Basic,
    Full
}

public record CaptureGranularity(
    RegisterPreset Registers = RegisterPreset.None,
    bool CallStack = false,
    bool Variables = true
);
