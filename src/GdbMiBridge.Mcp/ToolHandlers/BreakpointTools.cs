using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class BreakpointTools(GdbSession session)
{
    [McpServerTool, Description("Set a breakpoint at a function, file:line, or address. Returns a BreakpointConfig with ID for later remove/enable/disable. When capture=true, the granularity params below control WHAT state is captured on hit.")]
    public async Task<BreakpointConfig> SetBreakpoint(
        [Description("Function name ('main'), file:line ('test.c:47'), or address ('*0x555...').")] string location,
        [Description("Enable auto-capture when this breakpoint is hit. Set to false to disable all capture for this bp. When true, capture_registers/capture_call_stack/capture_variables control what is captured.")] bool capture = true,
        [Description("What to do when hit: 'break' stops and waits for Agent, 'go' auto-continues (useful for sampling/tracing without stopping).")] string action = "break",
        [Description("Optional GDB condition expression. Breakpoint only fires when condition is true, e.g. 'i == 7' or 'x > 100'.")] string? condition = null,
        [Description("Which registers to capture on hit: 'none' (skip registers — fastest), 'basic' (only common GPRs/control regs, ~16 on x64), 'full' (all 176+ registers including SIMD/vector).")] string capture_registers = "none",
        [Description("Whether to capture the call stack (backtrace) on hit. Stack has ~2 frames when at a leaf function, more for nested calls.")] bool capture_call_stack = false,
        [Description("Whether to capture local variables and function arguments on hit. On by default. Requires GDB to fetch variable values.")] bool capture_variables = true)
    {
        var granularity = capture ? ToolHelpers.FromToolParams(capture_registers, capture_call_stack, capture_variables) : null;
        return await session.SetBreakpointAsync(location, capture, granularity, action, condition);
    }

    [McpServerTool, Description("Remove a breakpoint by ID.")]
    public async Task<bool> RemoveBreakpoint(
        [Description("Breakpoint ID.")] string id)
        => await session.RemoveBreakpointAsync(id);

    [McpServerTool, Description("Enable a breakpoint.")]
    public async Task<bool> EnableBreakpoint(
        [Description("Breakpoint ID.")] string id)
        => await session.EnableBreakpointAsync(id, true);

    [McpServerTool, Description("Disable a breakpoint without removing it.")]
    public async Task<bool> DisableBreakpoint(
        [Description("Breakpoint ID.")] string id)
        => await session.EnableBreakpointAsync(id, false);

    [McpServerTool, Description("List all breakpoints in the current session.")]
    public List<BreakpointConfig> ListBreakpoints()
        => session.Breakpoints.GetAll();

    [McpServerTool, Description("Set a hardware data breakpoint (watchpoint) that triggers on memory access. Use resolve_symbol first to find a variable's address. Watchpoints are limited (typically 4 hardware slots). Returns a BreakpointConfig with ID.")]
    public async Task<BreakpointConfig> SetHardwareBreakpoint(
        [Description("Hex address to watch, e.g. '0x601040'. Use resolve_symbol to get a variable's address.")] string address,
        [Description("Trigger on 'write' (default), 'read', or 'access' (read+write).")] string access = "write",
        [Description("Size of the watched region in bytes: 1, 2, 4, or 8 (default 4).")] int size = 4,
        [Description("Enable auto-capture when this watchpoint triggers. Same semantics as set_breakpoint: granularity params below control what is captured.")] bool capture = true,
        [Description("Which registers to capture on trigger: 'none' (fastest), 'basic' (common GPRs), or 'full' (all registers).")] string capture_registers = "none",
        [Description("Whether to capture the call stack on trigger.")] bool capture_call_stack = false,
        [Description("Whether to capture local variables and function arguments on trigger.")] bool capture_variables = true)
    {
        var granularity = capture ? ToolHelpers.FromToolParams(capture_registers, capture_call_stack, capture_variables) : null;
        return await session.SetHardwareBreakpointAsync(address, access, size, capture, granularity);
    }
}
