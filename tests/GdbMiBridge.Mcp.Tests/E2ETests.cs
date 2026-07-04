using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace GdbMiBridge.Mcp.Tests;

public class E2ETests
{
    private readonly ITestOutputHelper _output;

    public E2ETests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task SetBreakpoint_Go_Break()
    {
        // 1. Create session
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<GdbSession>();
        using var session = new GdbSession(logger);

        // 2. Launch test program
        var info = await session.CreateAsync(
            exe: "/tmp/test_target_linux",
            args: null,
            workDir: null,
            stopAtEntry: true);
        _output.WriteLine($"Session created: pid={info.ProcessId}");

        Assert.Equal("create", info.Type);

        // 3. Set breakpoint on after_loop
        var bp = await session.SetBreakpointAsync(
            loc: "after_loop",
            capture: false,
            action: "break",
            cond: null);
        _output.WriteLine($"Breakpoint set: #{bp.BpNumber} at {bp.Location}");

        Assert.Equal("break", bp.Action);

        // 4. Go — continue execution until breakpoint hit
        var reason = await session.GoAsync(timeoutMs: 10000);
        _output.WriteLine($"Go returned: {reason}");

        Assert.Equal("breakpoint-hit", reason);

        // 5. Verify state
        var status = await session.StatusAsync();
        _output.WriteLine($"Status: {status.State}");
        Assert.Equal("Stopped", status.State);

        // 6. Clean up
        await session.TerminateAsync();
        _output.WriteLine("Session terminated");
    }
}
