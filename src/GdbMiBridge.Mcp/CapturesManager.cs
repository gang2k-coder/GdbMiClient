namespace GdbMiBridge.Mcp;

/// <summary>
/// Accumulates capture snapshots during a session.
/// Single-threaded — only accessed from GdbSession consumer thread.
/// </summary>
public class CapturesManager
{
    private readonly List<CaptureResult> _captures = new();

    public void Add(CaptureResult capture) => _captures.Add(capture);

    public IReadOnlyList<CaptureResult> GetAll() => _captures.AsReadOnly();

    public void Clear() => _captures.Clear();
}
