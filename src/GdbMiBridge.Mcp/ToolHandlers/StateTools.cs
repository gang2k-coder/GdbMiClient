using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class StateTools(GdbSession session)
{
    [McpServerTool, Description("Get CPU register values with human-readable names (e.g. 'rax' on x64, 'x0' on ARM64). Use preset='basic' for only the most commonly needed registers (~16), or 'full' for all 176+ registers including SIMD/vector.")]
    public async Task<Dictionary<string, string>> GetRegisters(
        [Description("Register preset: 'full' (default, all registers including SIMD/vector) or 'basic' (common GPRs, flags, and control registers only).")] string preset = "full")
        => await session.GetRegistersAsync(ToolHelpers.ParseRegisterPreset(preset));

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

    [McpServerTool, Description("Get local variables AND function arguments for a stack frame. Frame 0 is the current/innermost frame. Returns name, type, and value for each variable.")]
    public async Task<List<VariableInfo>> GetLocalVariables(
        [Description("Frame index (0 = current frame). Higher numbers go up the call stack.")] int frameIndex = 0)
        => await session.GetLocalVariablesAsync(frameIndex);

    [McpServerTool, Description("Manually capture program state at the current stop point. Unlike breakpoint auto-capture, this captures on-demand whenever you need a snapshot. By default captures variables only; override with parameters for registers and call stack.")]
    public async Task<CaptureResult> CaptureState(
        [Description("Which registers to include: 'none' (default, skip registers), 'basic' (common GPRs only), or 'full' (all registers including SIMD/vector).")] string registers = "none",
        [Description("Whether to include the call stack (backtrace) in this capture.")] bool call_stack = false,
        [Description("Whether to include local variables and function arguments in this capture.")] bool variables = true)
    {
        var granularity = ToolHelpers.FromToolParams(registers, call_stack, variables);
        return await session.CaptureStateAsync(granularity);
    }

    [McpServerTool, Description("Get all accumulated capture snapshots.")]
    public List<CaptureResult> GetCaptures()
        => session.Captures.GetAll().ToList();

    [McpServerTool, Description("Clear all accumulated capture snapshots.")]
    public string ClearCaptures()
    {
        session.Captures.Clear();
        return "cleared";
    }

    [McpServerTool, Description("Configure the default capture granularity for the entire session. This applies to ALL breakpoints that do not specify their own granularity override. Call this once at session start to set your debugging strategy — e.g., 'I only need variables' (fast) or 'I need everything' (detailed). Individual breakpoints can still override with set_breakpoint's capture_registers/capture_call_stack/capture_variables params.")]
    public string SetDefaultCaptureGranularity(
        [Description("Register preset for default captures: 'none' (fastest, skip registers), 'basic' (common GPRs ~16), or 'full' (all 176+ registers including SIMD).")] string registers = "none",
        [Description("Whether to capture call stack by default for every capture.")] bool call_stack = false,
        [Description("Whether to capture local variables and function arguments by default. On by default — this is usually what you want for debugging.")] bool variables = true)
    {
        session.DefaultGranularity = new CaptureGranularity(
            ToolHelpers.ParseRegisterPreset(registers), call_stack, variables);
        return "ok";
    }

    [McpServerTool, Description("Query the current default capture granularity. Returns a CaptureGranularity object with Registers (none/basic/full), CallStack (bool), and Variables (bool) fields. Use this before set_default_capture_granularity to check current settings.")]
    public CaptureGranularity GetDefaultCaptureGranularity()
        => session.DefaultGranularity;
}
