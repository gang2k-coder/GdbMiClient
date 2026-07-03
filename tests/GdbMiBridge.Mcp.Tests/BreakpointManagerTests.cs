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
        Assert.Equal(2, m.GetAll().Count);
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
