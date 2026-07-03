using Xunit;

namespace GdbMi.Tests;

public class GdbCommandFactoryTests
{
    [Fact]
    public void EscapeQuotes_EscapesDoubleQuotes()
        => Assert.Equal("hello \\\"world\\\"",
            GdbCommandFactory.EscapeQuotes("hello \"world\""));

    [Fact]
    public void PreparePath_UnixFormat_ConvertsSlashes()
    {
        GdbCommandFactory.PreparePath(@"C:\src\main.c", useUnixFormat: true, out var p);
        Assert.Equal("C:/src/main.c", p);
    }

    [Fact]
    public void PreparePath_WithSpaces_RequiresQuotes()
        => Assert.True(GdbCommandFactory.PreparePath(
            "/home/user/my code/main.c", useUnixFormat: false, out _));

    [Fact]
    public void PreparePath_WithoutSpaces_NoQuotes()
        => Assert.False(GdbCommandFactory.PreparePath(
            "/home/user/main.c", useUnixFormat: false, out _));
}
