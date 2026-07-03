using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class StateTools(GdbSession session)
{
    [McpServerTool, Description("Get all CPU register values.")]
    public async Task<Dictionary<string, string>> GetRegisters()
        => await session.GetRegistersAsync();

    [McpServerTool, Description("Get the program counter with symbol info.")]
    public async Task<ProgramCounterInfo> GetProgramCounter()
        => await session.GetProgramCounterAsync();

    [McpServerTool, Description("Read memory at the given address.")]
    public async Task<MemoryData> ReadMemory(
        [Description("Hex address.")] string address,
        [Description("Bytes to read.")] int size = 64)
        => await session.ReadMemoryAsync(address, size);

    [McpServerTool, Description("Get the call stack.")]
    public async Task<List<GdbMi.FrameInfo>> GetCallStack(
        [Description("Max frames.")] int maxFrames = 20)
        => await session.GetCallStackAsync(maxFrames);

    [McpServerTool, Description("List all threads.")]
    public async Task<List<ThreadInfo>> ListThreads()
        => await session.ListThreadsAsync();

    [McpServerTool, Description("Get local variables for a stack frame.")]
    public async Task<List<VariableInfo>> GetLocalVariables(
        [Description("Frame index (0 = current).")] int frameIndex = 0)
        => await session.GetLocalVariablesAsync(frameIndex);

    [McpServerTool, Description("Manually capture current program state.")]
    public async Task<CaptureResult> CaptureState()
        => await session.CaptureStateAsync();

    [McpServerTool, Description("Get all accumulated capture snapshots.")]
    public List<CaptureResult> GetCaptures()
        => session.Captures.GetAll().ToList();

    [McpServerTool, Description("Clear all accumulated capture snapshots.")]
    public string ClearCaptures()
    {
        session.Captures.Clear();
        return "cleared";
    }
}
