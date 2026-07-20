namespace GdbMiBridge.Mcp;

public static class ToolHelpers
{
    public static RegisterPreset ParseRegisterPreset(string value) => value?.ToLowerInvariant() switch
    {
        "basic" => RegisterPreset.Basic,
        "full" => RegisterPreset.Full,
        _ => RegisterPreset.None
    };

    public static CaptureGranularity FromToolParams(string registers, bool callStack, bool variables)
        => new(ParseRegisterPreset(registers), callStack, variables);
}
