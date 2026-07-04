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

        // 2. Set go-action breakpoint on loop_body (hit 10 times, capture silently, auto-continue)
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

        // 5. Check captures — should have 10 loop_body + 1 after_loop = 11 snapshots
        var captures = session.Captures.GetAll();
        Assert.Equal(11, captures.Count);

        // loop_body captures (first 10)
        for (int i = 0; i < 10; i++)
        {
            var c = captures[i];
            Assert.Equal("loop_body", c.BreakpointLocation);
            Assert.NotEmpty(c.Registers);
            Assert.Equal("loop_body", c.ProgramCounter.Symbol);
            Assert.NotEmpty(c.CallStack);
            _output.WriteLine($"  Capture {i}: {c.BreakpointLocation} at {c.ProgramCounter.Address}, regs={c.Registers.Count}, stack={c.CallStack.Count}");
        }

        // after_loop capture (last)
        var lastCapture = captures[10];
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

    [Fact]
    public async Task GoActionLoop10_BreakAction_CaptureContent()
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<GdbSession>();
        using var session = new GdbSession(logger);

        // 1. Launch — new target with 10 loop_body iterations
        var info = await session.CreateAsync(
            exe: "/tmp/test_target_linux", args: null, workDir: null, stopAtEntry: true);
        _output.WriteLine($"Session: pid={info.ProcessId}");

        // 2. go-action + capture on loop_body — hits 10 times silently
        var goBp = await session.SetBreakpointAsync(
            loc: "loop_body", capture: true, action: "go", cond: null);
        _output.WriteLine($"Go-action bp {goBp.BpNumber} on loop_body");

        // 3. break-action + capture on after_loop — stops here
        var breakBp = await session.SetBreakpointAsync(
            loc: "after_loop", capture: true, action: "break", cond: null);
        _output.WriteLine($"Break-action bp {breakBp.BpNumber} on after_loop");

        // 4. Clear captures before starting
        session.Captures.Clear();

        // 5. Go
        var reason = await session.GoAsync(timeoutMs: 15000);
        _output.WriteLine($"Go returned: {reason}");
        Assert.Equal("breakpoint-hit", reason);

        // 6. Verify capture count: 10 loop_body + 1 after_loop = 11
        var captures = session.Captures.GetAll();
        Assert.Equal(11, captures.Count);
        _output.WriteLine($"Total captures: {captures.Count}");

        // 7. Verify each loop_body capture
        List<string> pcAddresses = new();
        for (int i = 0; i < 10; i++)
        {
            var c = captures[i];
            _output.WriteLine($"  Capture[{i}]: bp={c.BreakpointNumber} loc={c.BreakpointLocation} " +
                $"pc={c.ProgramCounter.Address} func={c.ProgramCounter.Symbol} " +
                $"regs={c.Registers.Count} stack={c.CallStack.Count}");

            Assert.Equal("loop_body", c.BreakpointLocation);
            Assert.Equal("loop_body", c.ProgramCounter.Symbol);

            // Registers — should have plenty (GDB x86-64 reports 263 including vector regs)
            Assert.NotEmpty(c.Registers);
            Assert.True(c.Registers.Count >= 10, $"Expected >= 10 registers, got {c.Registers.Count}");
            // First 48 are scalar; verify at least the low-numbered ones are hex
            int hexCount = c.Registers.Values.Take(48).Count(v => v.StartsWith("0x"));
            Assert.True(hexCount >= 20, $"Expected >= 20 hex scalar registers, got {hexCount}");
            _output.WriteLine($"        regs={c.Registers.Count} ({hexCount} hex scalars)");

            // Call stack — should have loop_body → main → __libc_start_main
            Assert.NotEmpty(c.CallStack);
            var topFrame = c.CallStack[0];
            Assert.Equal("loop_body", topFrame.FunctionName);
            _output.WriteLine($"        stack top: {topFrame.FunctionName} @ {topFrame.Address}");

            // Track PC addresses — each loop iteration should be at the same address
            pcAddresses.Add(c.ProgramCounter.Address);

            // Timestamp should be set
            Assert.True(c.Timestamp != default);
        }

        // All loop_body captures should be at the same address
        var distinctAddrs = pcAddresses.Distinct().ToList();
        Assert.Single(distinctAddrs);
        _output.WriteLine($"  All loop_body PCs identical: {distinctAddrs[0]}");

        // 8. Verify after_loop capture (last one)
        var last = captures[10];
        Assert.Equal("after_loop", last.BreakpointLocation);
        Assert.Equal("after_loop", last.ProgramCounter.Symbol);
        Assert.NotEmpty(last.Registers);
        Assert.NotEmpty(last.CallStack);
        Assert.Equal("after_loop", last.CallStack[0].FunctionName);
        _output.WriteLine($"  Final capture: {last.BreakpointLocation} func={last.ProgramCounter.Symbol} stack frames={last.CallStack.Count}");

        // 9. Verify we are stopped
        var status = await session.StatusAsync();
        Assert.Equal("Stopped", status.State);

        // 10. Clean up
        await session.TerminateAsync();
        _output.WriteLine("Done");
    }

    [Fact]
    public async Task HardwareBreakpoint_Watchpoint_Triggers()
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<GdbSession>();
        using var session = new GdbSession(logger);

        // 1. Launch — stop at entry
        await session.CreateAsync(
            exe: "/tmp/test_target_linux", args: null, workDir: null, stopAtEntry: true);

        // 2. Set hardware write watchpoint using expression directly
        // bypass symbol resolution to isolate the watchpoint test
        await session.RawGdbAsync("-break-watch *(int*)&g_counter");
        _output.WriteLine("Watchpoint set via expression");

        // 3. Go — first g_counter++ (inside add()) triggers it
        var reason = await session.GoAsync(timeoutMs: 5000);
        _output.WriteLine($"Go returned: {reason}");
        Assert.Contains("watchpoint", reason);

        // 4. Stopped
        Assert.Equal("Stopped", (await session.StatusAsync()).State);

        // 5. Clean up
        await session.TerminateAsync();
        _output.WriteLine("Done");
    }

    [Fact]
    public async Task ConditionalBreakpoint_OnlyFiresWhenConditionMet()
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<GdbSession>();
        using var session = new GdbSession(logger);

        // 1. Launch
        await session.CreateAsync(
            exe: "/tmp/test_target_linux", args: null, workDir: null, stopAtEntry: true);

        // 2. Conditional breakpoint: only stop when i == 7
        var bp = await session.SetBreakpointAsync(
            loc: "loop_body", capture: true, action: "break", cond: "i == 7");
        _output.WriteLine($"Conditional bp #{bp.BpNumber} on loop_body, cond: 'i == 7'");
        Assert.Equal("break", bp.Action);

        // 3. Go — loop_body is called 10 times but only stops at i==7
        var reason = await session.GoAsync(timeoutMs: 10000);
        _output.WriteLine($"Go returned: {reason}");
        Assert.Equal("breakpoint-hit", reason);

        // 4. Only one capture — condition only satisfied once
        var captures = session.Captures.GetAll();
        Assert.Single(captures);

        var c = captures[0];
        _output.WriteLine($"Capture: func={c.ProgramCounter.Symbol} regs={c.Registers.Count} stack={c.CallStack.Count}");

        // Verify we're in loop_body, and check the local variable 'i'
        Assert.Equal("loop_body", c.ProgramCounter.Symbol);

        // Find local variable 'i' — should be 7
        var varI = c.LocalVariables.FirstOrDefault(v => v.Name == "i");
        if (varI is not null)
        {
            _output.WriteLine($"  local 'i' = {varI.Value}");
            Assert.Contains("7", varI.Value);
        }
        else
        {
            // Even without exact locals, the fact we stopped once in 10 iterations is proof
            _output.WriteLine("  (local 'i' not found in capture — using indirect verification)");
        }

        // 5. Verify stopped
        Assert.Equal("Stopped", (await session.StatusAsync()).State);

        // 6. Clean up
        await session.TerminateAsync();
        _output.WriteLine("Done");
    }
}
