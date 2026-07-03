using Xunit;

namespace GdbMiBridge.Mcp.Tests;

public class CapturesManagerTests
{
    private static CaptureResult MakeCapture(string bpNum) => new(
        bpNum, $"loc_{bpNum}", new(), new("", "", ""),
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
