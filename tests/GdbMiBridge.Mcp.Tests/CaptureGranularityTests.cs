using GdbMiBridge.Mcp;

namespace GdbMiBridge.Mcp.Tests;

public class CaptureGranularityTests
{
    [Fact]
    public void Default_IsVariablesOnly()
    {
        var g = new CaptureGranularity();

        Assert.Equal(RegisterPreset.None, g.Registers);
        Assert.False(g.CallStack);
        Assert.True(g.Variables);
    }

    [Fact]
    public void FullCapture_AllOn()
    {
        var g = new CaptureGranularity(
            Registers: RegisterPreset.Full,
            CallStack: true,
            Variables: true);

        Assert.Equal(RegisterPreset.Full, g.Registers);
        Assert.True(g.CallStack);
        Assert.True(g.Variables);
    }

    [Fact]
    public void BasicRegisters_NoStack_NoVariables()
    {
        var g = new CaptureGranularity(
            Registers: RegisterPreset.Basic,
            CallStack: false,
            Variables: false);

        Assert.Equal(RegisterPreset.Basic, g.Registers);
        Assert.False(g.CallStack);
        Assert.False(g.Variables);
    }

    [Fact]
    public void ValueEquality_Works()
    {
        var a = new CaptureGranularity(RegisterPreset.Basic, true, true);
        var b = new CaptureGranularity(RegisterPreset.Basic, true, true);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ValueEquality_Differs()
    {
        var a = new CaptureGranularity(RegisterPreset.None, false, true);
        var b = new CaptureGranularity(RegisterPreset.Full, true, true);

        Assert.NotEqual(a, b);
    }
}
