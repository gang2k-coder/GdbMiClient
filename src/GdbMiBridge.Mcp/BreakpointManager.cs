namespace GdbMiBridge.Mcp;

/// <summary>
/// Registry of all breakpoints in the current session.
/// Single-threaded — only accessed from GdbSession consumer thread.
/// </summary>
public class BreakpointManager
{
    private readonly Dictionary<string, BreakpointConfig> _bps = new();

    public void Register(string bpNumber, BreakpointConfig config)
    {
        _bps[bpNumber] = config with { BpNumber = bpNumber };
    }

    public void Remove(string bpNumber) => _bps.Remove(bpNumber);

    public void SetEnabled(string bpNumber, bool enabled)
    {
        if (_bps.TryGetValue(bpNumber, out var config))
            _bps[bpNumber] = config with { Enabled = enabled };
    }

    public BreakpointConfig? FindByNumber(string bpNumber)
        => _bps.TryGetValue(bpNumber, out var c) ? c : null;

    public List<BreakpointConfig> GetAll() => _bps.Values.ToList();

    public void Clear() => _bps.Clear();

    /// <summary>
    /// Called when *stopped with reason="breakpoint-hit".
    /// Returns (shouldCapture, granularity, shouldContinue).
    /// </summary>
    public (bool ShouldCapture, CaptureGranularity? Granularity, bool ShouldContinue) OnHit(string bpNumber)
    {
        var config = FindByNumber(bpNumber);
        if (config is null) return (false, null, false);
        return (config.Capture, config.Granularity, config.Action == "go");
    }
}
