using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class RawTools(GdbSession session)
{
    [McpServerTool, Description("Execute an arbitrary GDB command (MI or CLI).")]
    public async Task<string> RawGdb(
        [Description("The GDB command to execute.")] string command)
        => await session.RawGdbAsync(command);
}
