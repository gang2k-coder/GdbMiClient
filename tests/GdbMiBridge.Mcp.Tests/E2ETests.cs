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

    [Fact]
    public async Task GoActionCapture_BreakActionCapture()
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<GdbSession>();
        using var session = new GdbSession(logger);

        // 1. Launch
        var info = await session.CreateAsync(
            exe: "/tmp/test_target_linux", args: null, workDir: null, stopAtEntry: true);
        _output.WriteLine($"Session: pid={info.ProcessId}");

        // 2. Set go-action breakpoint on loop_body (hit 5 times, capture silently, auto-continue)
        var goBp = await session.SetBreakpointAsync(
            loc: "loop_body", capture: true, action: "go", cond: null);
        _output.WriteLine($"Go-action bp: #{goBp.BpNumber}");

        Assert.Equal("go", goBp.Action);
        Assert.True(goBp.Capture);

        // 3. Set break-action breakpoint on after_loop (hit once, capture, stop)
        var breakBp = await session.SetBreakpointAsync(
            loc: "after_loop", capture: true, action: "break", cond: null);
        _output.WriteLine($"Break-action bp: #{breakBp.BpNumber}");

        Assert.Equal("break", breakBp.Action);
        Assert.True(breakBp.Capture);

        // 4. Go — loop_body hits 5 times silently, after_loop hits and stops
        var reason = await session.GoAsync(timeoutMs: 10000);
        _output.WriteLine($"Go returned: {reason}");
        Assert.Equal("breakpoint-hit", reason);

        // 5. Check captures — should have 5 loop_body + 1 after_loop = 6 snapshots
        var captures = session.Captures.GetAll();
        Assert.Equal(6, captures.Count);

        // loop_body captures (first 5)
        for (int i = 0; i < 5; i++)
        {
            var c = captures[i];
            Assert.Equal("loop_body", c.BreakpointLocation);
            Assert.NotEmpty(c.Registers);
            Assert.Equal("loop_body", c.ProgramCounter.Symbol);
            Assert.NotEmpty(c.CallStack);
            _output.WriteLine($"  Capture {i}: {c.BreakpointLocation} at {c.ProgramCounter.Address}, regs={c.Registers.Count}, stack={c.CallStack.Count}");
        }

        // after_loop capture (last)
        var lastCapture = captures[5];
        Assert.Equal("after_loop", lastCapture.BreakpointLocation);
        Assert.NotEmpty(lastCapture.Registers);
        Assert.Equal("after_loop", lastCapture.ProgramCounter.Symbol);
        _output.WriteLine($"  Final capture: {lastCapture.BreakpointLocation} at {lastCapture.ProgramCounter.Address}");

        // 6. Verify stopped state — should be at after_loop
        var status = await session.StatusAsync();
        Assert.Equal("Stopped", status.State);

        // 7. Clean up
        await session.TerminateAsync();
        _output.WriteLine("Done");
    }
}
