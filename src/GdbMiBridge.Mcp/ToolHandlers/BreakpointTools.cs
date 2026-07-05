using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class BreakpointTools(GdbSession session)
{
    [McpServerTool, Description("Set a breakpoint. Location can be: function name ('add'), file:line ('test.c:47'), or address ('*0x555...'). Returns breakpoint config with ID (use to remove/enable/disable).")]
    public async Task<BreakpointConfig> SetBreakpoint(
        [Description("Function name, file:line, or address (prefix addresses with '*').")] string location,
        [Description("Auto-capture registers/stack/locals when hit.")] bool capture = true,
        [Description("'break' = stop and wait, 'go' = auto-continue after capture.")] string action = "break",
        [Description("GDB condition expression, e.g. 'i > 5'.")] string? condition = null)
        => await session.SetBreakpointAsync(location, capture, action, condition);

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

    [McpServerTool, Description("Set a hardware data breakpoint (watchpoint) that triggers when memory is accessed. Use resolve_symbol first to get the address (e.g. &g_counter → 0x555555558014).")]
    public async Task<BreakpointConfig> SetHardwareBreakpoint(
        [Description("Hex address, e.g. '0x601040'. Use resolve_symbol to find a variable's address.")] string address,
        [Description("Access type: 'write', 'read', or 'access'.")] string access = "write",
        [Description("Watch size in bytes: 1, 2, 4, 8.")] int size = 4,
        [Description("Capture state when hit.")] bool capture = true)
        => await session.SetHardwareBreakpointAsync(address, access, size, capture);
}
