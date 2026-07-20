using GdbMiBridge.Mcp;

namespace GdbMiBridge.Mcp.Tests;

public class ToolHelpersTests
{
    [Theory]
    [InlineData("none", RegisterPreset.None)]
    [InlineData("None", RegisterPreset.None)]
    [InlineData("NONE", RegisterPreset.None)]
    [InlineData("basic", RegisterPreset.Basic)]
    [InlineData("Basic", RegisterPreset.Basic)]
    [InlineData("BASIC", RegisterPreset.Basic)]
    [InlineData("full", RegisterPreset.Full)]
    [InlineData("Full", RegisterPreset.Full)]
    [InlineData("FULL", RegisterPreset.Full)]
    public void ParseRegisterPreset_ValidValues(string input, RegisterPreset expected)
    {
        Assert.Equal(expected, ToolHelpers.ParseRegisterPreset(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("all")]
    [InlineData("minimal")]
    public void ParseRegisterPreset_InvalidValues_DefaultToNone(string input)
    {
        Assert.Equal(RegisterPreset.None, ToolHelpers.ParseRegisterPreset(input));
    }

    [Fact]
    public void FromToolParams_AllFull()
    {
        var g = ToolHelpers.FromToolParams("full", true, true);

        Assert.Equal(RegisterPreset.Full, g.Registers);
        Assert.True(g.CallStack);
        Assert.True(g.Variables);
    }

    [Fact]
    public void FromToolParams_VariablesOnly()
    {
        var g = ToolHelpers.FromToolParams("none", false, true);

        Assert.Equal(RegisterPreset.None, g.Registers);
        Assert.False(g.CallStack);
        Assert.True(g.Variables);
    }

    [Fact]
    public void FromToolParams_BasicRegistersOnly()
    {
        var g = ToolHelpers.FromToolParams("basic", false, false);

        Assert.Equal(RegisterPreset.Basic, g.Registers);
        Assert.False(g.CallStack);
        Assert.False(g.Variables);
    }
}
