using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class BreakpointTools(GdbSession session)
{
    [McpServerTool, Description("Set a breakpoint at a function, file:line, or address.")]
    public async Task<BreakpointConfig> SetBreakpoint(
        [Description("Function name, file:line, or address.")] string location,
        [Description("Capture state when hit.")] bool capture = true,
        [Description("'go' = auto-continue, 'break' = stop and wait.")] string action = "break",
        [Description("Condition expression.")] string? condition = null)
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

    [McpServerTool, Description("Set a hardware data breakpoint (watchpoint) at an address.")]
    public async Task<BreakpointConfig> SetHardwareBreakpoint(
        [Description("Hex address, e.g. '0x601040'.")] string address,
        [Description("Access type: 'write', 'read', or 'access'.")] string access = "write",
        [Description("Watch size in bytes: 1, 2, 4, 8.")] int size = 4,
        [Description("Capture state when hit.")] bool capture = true)
        => await session.SetHardwareBreakpointAsync(address, access, size, capture);
}
