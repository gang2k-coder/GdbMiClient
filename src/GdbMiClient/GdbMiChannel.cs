using Microsoft.Extensions.Logging;

namespace GdbMi;

/// <summary>
/// Orchestration layer:
/// - SemaphoreSlim(1,1) serializes command execution
/// - Background ReadLoop feeds stdout lines to GdbMiClient.ProcessLine()
/// Thread-safe — callable from any MCP tool thread.
/// </summary>
public class GdbMiChannel : IDisposable
{
    private readonly GdbMiClient _client;
    private readonly ITransport _transport;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _cmdSem = new(1, 1);
    private Task? _readLoopTask;
    private CancellationTokenSource? _cts;

    public GdbMiClient Client => _client;

    public GdbMiChannel(GdbMiClient client, ITransport transport,
        ILogger<GdbMiChannel>? logger = null)
    {
        _client = client;
        _transport = transport;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _client.ConnectAsync(ct);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readLoopTask = ReadLoopAsync(_cts.Token);
    }

    /// <summary>Send a command and wait for the response. Thread-safe.</summary>
    public async Task<Results> SendCommandAsync(MICommand command, CancellationToken ct = default)
    {
        await _cmdSem.WaitAsync(ct);
        try { return await _client.ExecuteAsync(command, ct); }
        finally { _cmdSem.Release(); }
    }

    /// <summary>Wait for next *stopped event. Thread-safe. Does NOT use semaphore.</summary>
    public Task<StopEvent> WaitForStopAsync(CancellationToken ct = default)
        => _client.WaitForStopAsync(ct);

    public IAsyncEnumerable<string> GetOutputStream(CancellationToken ct = default)
        => _client.GetOutputStream(ct);

    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        await _cmdSem.WaitAsync(ct);
        try { await _client.DisconnectAsync(CancellationToken.None); }
        finally { _cmdSem.Release(); }
        _transport.Close();
    }

    public void Dispose()
    {
        _cts?.Cancel(); _cts?.Dispose();
        _cmdSem?.Dispose(); _client?.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_transport.IsClosed)
            {
                var line = await _transport.ReadLineAsync(ct);
                if (line is null) break;
                _client.ProcessLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger?.LogError(ex, "ReadLoop error"); }
        finally { _logger?.LogInformation("ReadLoop ended"); }
    }
}
