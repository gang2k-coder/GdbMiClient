namespace GdbMi;

public interface ITransport
{
    Task ConnectAsync(CancellationToken ct);
    Task SendAsync(string line, CancellationToken ct);
    Task<string?> ReadLineAsync(CancellationToken ct);
    void Close();
    bool IsClosed { get; }
    int? DebuggerPid { get; }
}
