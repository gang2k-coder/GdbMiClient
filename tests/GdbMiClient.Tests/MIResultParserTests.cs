using Xunit;
using GdbMi;

namespace GdbMi.Tests;

public class MIResultParserTests
{
    private readonly MIResultParser _parser = new();

    [Fact]
    public void ParseCommandOutput_DoneWithNoFields_ReturnsDone()
    {
        var r = _parser.ParseCommandOutput("done");
        Assert.Equal(ResultClass.Done, r.ResultClass);
    }

    [Fact]
    public void ParseCommandOutput_DoneWithBkpt_ParsesAllFields()
    {
        var r = _parser.ParseCommandOutput(
            "done,bkpt={number=\"1\",addr=\"0x401000\",file=\"main.c\",line=\"42\"}");
        Assert.Equal(ResultClass.Done, r.ResultClass);
        var bkpt = r.Find<TupleValue>("bkpt");
        Assert.Equal("1", bkpt.FindString("number"));
        Assert.Equal("0x401000", bkpt.FindString("addr"));
        Assert.Equal(42, bkpt.FindInt("line"));
    }

    [Fact]
    public void ParseCommandOutput_Error_ParsesMessage()
    {
        var r = _parser.ParseCommandOutput("error,msg=\"No symbol table\"");
        Assert.Equal(ResultClass.Error, r.ResultClass);
        Assert.Equal("No symbol table", r.FindString("msg"));
    }

    [Fact]
    public void ParseCommandOutput_Running_ReturnsRunning()
    {
        var r = _parser.ParseCommandOutput("running");
        Assert.Equal(ResultClass.Running, r.ResultClass);
    }

    [Fact]
    public void ParseCommandOutput_MultipleFields_ParsesAll()
    {
        var r = _parser.ParseCommandOutput("done,bkpt={number=\"1\"},thread-id=\"42\"");
        Assert.Equal(ResultClass.Done, r.ResultClass);
        Assert.Equal("42", r.FindString("thread-id"));
    }

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("\"hello\\nworld\"", "hello\nworld")]
    [InlineData("\"hello\\tworld\"", "hello\tworld")]
    [InlineData("\"with \\\"quotes\\\"\"", "with \"quotes\"")]
    [InlineData("plain text", "plain text")]
    [InlineData("", "")]
    public void ParseCString_DecodesCorrectly(string input, string expected)
    {
        Assert.Equal(expected, _parser.ParseCString(input));
    }

    [Fact]
    public void ParseResultList_StoppedEvent_ParsesCorrectly()
    {
        var r = _parser.ParseResultList(
            "reason=\"breakpoint-hit\",bkptno=\"1\"," +
            "frame={addr=\"0x401000\",func=\"main\"}");
        Assert.Equal("breakpoint-hit", r.FindString("reason"));
        Assert.Equal("1", r.FindString("bkptno"));
        Assert.Equal("main", r.Find<TupleValue>("frame").FindString("func"));
    }

    [Fact]
    public void ParseResultList_StackFrames_ParsesArray()
    {
        var r = _parser.ParseResultList(
            "stack=[frame={level=\"0\",func=\"main\"}," +
            "frame={level=\"1\",func=\"start\"}]");
        var frames = r.Find<ResultListValue>("stack").FindAll<TupleValue>("frame");
        Assert.Equal(2, frames.Length);
        Assert.Equal("0", frames[0].FindString("level"));
        Assert.Equal("1", frames[1].FindString("level"));
    }

    [Fact]
    public void ParseResultList_EmptyList_IsEmpty()
    {
        Assert.True(_parser.ParseResultList("stack=[]").Find<ListValue>("stack").IsEmpty());
    }

    [Fact]
    public void ParseResultList_ValueList_ParsesStrings()
    {
        var r = _parser.ParseResultList("register-names=[\"rax\",\"rbx\",\"rcx\"]");
        var names = r.Find<ValueListValue>("register-names").AsStrings;
        Assert.Equal(new[] { "rax", "rbx", "rcx" }, names);
    }

    [Fact]
    public void FindString_Missing_ThrowsMIResultFormatException()
    {
        Assert.Throws<MIResultFormatException>(() =>
            new Results(ResultClass.Done, new()).FindString("missing"));
    }

    [Fact]
    public void TryFindString_Missing_ReturnsEmpty()
    {
        Assert.Equal("", new Results(ResultClass.Done, new()).TryFindString("missing"));
    }

    [Fact]
    public void FindUint_HexValue_ParsesCorrectly()
    {
        var r = _parser.ParseCommandOutput("done,addr=\"0xff00ab12\"");
        Assert.Equal(0xff00ab12u, r.FindUint("addr"));
    }

    [Fact]
    public void ParseCommandOutput_EmptyString_ReturnsNone()
    {
        var r = _parser.ParseCommandOutput("");
        Assert.Equal(ResultClass.None, r.ResultClass);
    }

    [Fact]
    public void ParseResultList_MultiTuple_GdbMultipleBreakpoints()
    {
        var r = _parser.ParseResultList("bkpt={number=\"1\"},{number=\"2\"}");
        var value = r.Find<ValueListValue>("bkpt");
        Assert.Equal(2, value.Length);
    }
}
