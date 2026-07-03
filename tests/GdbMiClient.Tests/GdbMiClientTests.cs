using System.Collections.Concurrent;
using GdbMi;
using Xunit;

namespace GdbMi.Tests;

public class MockTransport : ITransport
{
    private readonly ConcurrentQueue<string> _sent = new();
    private readonly SemaphoreSlim _sentSignal = new(0);
    private readonly ConcurrentQueue<string> _inject = new();
    private readonly SemaphoreSlim _injectSignal = new(0);
    private volatile bool _isClosed;

    public bool IsClosed => _isClosed;
    public int? DebuggerPid => 12345;

    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;

    public Task SendAsync(string line, CancellationToken ct)
    {
        _sent.Enqueue(line);
        _sentSignal.Release();
        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        try { await _injectSignal.WaitAsync(ct); }
        catch (OperationCanceledException) { return null; }
        catch (ObjectDisposedException) { return null; }
        if (_isClosed) return null;
        _inject.TryDequeue(out var line);
        return line;
    }

    public void InjectLine(string line)
    {
        _inject.Enqueue(line);
        _injectSignal.Release();
    }

    public async Task<string> ReadSentLineAsync(CancellationToken ct = default)
    {
        await _sentSignal.WaitAsync(ct);
        _sent.TryDequeue(out var line);
        return line!;
    }

    public void Close()
    {
        _isClosed = true;
        _sentSignal.Dispose();
        _injectSignal.Dispose();
    }
}

public class GdbMiClientTests
{
    [Fact]
    public async Task ProcessLine_BasicStopped_Works()
    {
        var t = new MockTransport();
        var c = new GdbMiClient(t);
        await c.ConnectAsync(CancellationToken.None);
        c.ProcessLine("*stopped,reason=\"breakpoint-hit\",bkptno=\"1\"," +
            "frame={addr=\"0x401000\",func=\"main\"}");
        Assert.Equal(DebuggerState.Stopped, c.State);
    }

    [Fact]
    public async Task ExecuteAsync_SendsTokenAndCommand_MatchesResponse()
    {
        var t = new MockTransport();
        var c = new GdbMiClient(t);
        await c.ConnectAsync(CancellationToken.None);

        var task = c.ExecuteAsync(new MICommand("-break-insert", "-f main.c:42"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var line = await t.ReadSentLineAsync(cts.Token);
        Assert.EndsWith("-break-insert -f main.c:42", line);
        Assert.Matches(@"^\d{4}", line);

        string token = line[..4];
        // Simulate GDB response — call ProcessLine directly (the ReadLoop does this in production)
        c.ProcessLine($"{token}^done,bkpt={{number=\"1\",addr=\"0x401000\"}}");

        var result = await task;
        Assert.Equal(ResultClass.Done, result.ResultClass);
        Assert.Equal("1", result.Find<TupleValue>("bkpt").FindString("number"));
    }

    [Fact]
    public async Task ProcessLine_Stopped_SetsStateAndStopTcs()
    {
        var t = new MockTransport();
        var c = new GdbMiClient(t);
        await c.ConnectAsync(CancellationToken.None);
        Assert.Equal(DebuggerState.Running, c.State);

        var stopTask = c.WaitForStopAsync();
        c.ProcessLine("*stopped,reason=\"breakpoint-hit\",bkptno=\"1\"," +
            "frame={addr=\"0x401000\",func=\"main\"}");

        Assert.Equal(DebuggerState.Stopped, c.State);
        var evt = await stopTask;
        Assert.Equal("breakpoint-hit", evt.Reason);
        Assert.Equal("1", evt.BreakpointNumber);
        Assert.Equal("main", evt.Frame!.FunctionName);
    }

    [Fact]
    public async Task WaitForStop_SecondStop_UsesNewTcs()
    {
        var t = new MockTransport();
        var c = new GdbMiClient(t);
        await c.ConnectAsync(CancellationToken.None);

        c.ProcessLine("*stopped,reason=\"breakpoint-hit\",bkptno=\"1\"," +
            "frame={addr=\"0x401000\",func=\"main\"}");
        var evt1 = await c.WaitForStopAsync();
        Assert.Equal("1", evt1.BreakpointNumber);

        c.ProcessLine("*stopped,reason=\"exited-normally\"");
        var evt2 = await c.WaitForStopAsync();
        Assert.Equal("exited-normally", evt2.Reason);
    }

    [Fact]
    public async Task ProcessLine_ConsoleOutput_FlowsToStream()
    {
        var t = new MockTransport();
        var c = new GdbMiClient(t);
        await c.ConnectAsync(CancellationToken.None);

        c.ProcessLine("~\"hello\\n\"");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var iter = c.GetOutputStream(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await iter.MoveNextAsync());
        Assert.Equal("hello\n", iter.Current);
    }

    [Fact]
    public async Task ExecuteAsync_Error_ReturnsErrorResult()
    {
        var t = new MockTransport();
        var c = new GdbMiClient(t);
        await c.ConnectAsync(CancellationToken.None);

        var task = c.ExecuteAsync(new MICommand("-bad-cmd", ""));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        string line = await t.ReadSentLineAsync(cts.Token);
        string token = line[..4];

        c.ProcessLine($"{token}^error,msg=\"Undefined command\"");
        var result = await task;
        Assert.Equal(ResultClass.Error, result.ResultClass);
        Assert.Equal("Undefined command", result.FindString("msg"));
    }
}
