using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GdbMiClient;

public class LocalTransport : ITransport, IDisposable
{
    private readonly string _gdbPath;
    private readonly string _gdbArgs;
    private readonly ILogger? _logger;
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _isClosed;

    public int? DebuggerPid => _process?.Id;
    public bool IsClosed => _isClosed;

    public LocalTransport(string gdbPath = "gdb", string gdbArgs = "--interpreter=mi3",
        ILogger? logger = null)
    {
        _gdbPath = gdbPath;
        _gdbArgs = gdbArgs;
        _logger = logger;
    }

    public Task ConnectAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _gdbPath,
            Arguments = _gdbArgs,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _process = new Process { StartInfo = psi };
        _process.Start();
        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;
        _ = _process.StandardError.ReadToEndAsync();
        _logger?.LogDebug("GDB started: PID={Pid}", _process.Id);
        return Task.CompletedTask;
    }

    public async Task SendAsync(string line, CancellationToken ct)
    {
        if (_isClosed || _stdin is null) throw new ObjectDisposedException(nameof(LocalTransport));
        _logger?.LogTrace("-> {Line}", line);
        await _stdin.WriteLineAsync(line.AsMemory(), ct);
        await _stdin.FlushAsync(ct);
    }

    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        if (_isClosed || _stdout is null) return null;
        try
        {
            var line = await _stdout.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is not null) { line = line.TrimEnd(); _logger?.LogTrace("<- {Line}", line); }
            return line;
        }
        catch (OperationCanceledException) { return null; }
        catch (ObjectDisposedException) { return null; }
        catch (IOException) { return null; }
    }

    public void Close()
    {
        if (_isClosed) return;
        _isClosed = true;
        try { if (_process is { HasExited: false }) { _process.Kill(); _process.WaitForExit(3000); } }
        catch (Exception ex) { _logger?.LogWarning(ex, "Error closing GDB"); }
        try { _stdin?.Dispose(); } catch { }
        try { _stdout?.Dispose(); } catch { }
        try { _process?.Dispose(); } catch { }
    }

    public void Dispose() => Close();
}
