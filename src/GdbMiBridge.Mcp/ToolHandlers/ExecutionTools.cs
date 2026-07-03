using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class ExecutionTools(GdbSession session)
{
    [McpServerTool, Description("Continue execution until next stop event or timeout.")]
    public async Task<string> Go(
        [Description("Timeout in ms (0 = no timeout).")] int timeoutMs = 0)
        => await session.GoAsync(timeoutMs);

    [McpServerTool, Description("Single-step into the next instruction (enters function calls).")]
    public async Task<string> StepInto()
        => await session.StepIntoAsync();

    [McpServerTool, Description("Single-step over the current line (skips function calls).")]
    public async Task<string> StepOver()
        => await session.StepOverAsync();

    [McpServerTool, Description("Execute until the current function returns.")]
    public async Task<string> StepOut()
        => await session.StepOutAsync();

    [McpServerTool, Description("Run to a specified location using a temporary breakpoint.")]
    public async Task<string> GoTo(
        [Description("Function name, file:line, or address.")] string location)
        => await session.GoToAsync(location);
}
