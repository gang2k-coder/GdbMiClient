using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class SessionTools(GdbSession session)
{
    [McpServerTool, Description("Create a debugging session by launching an executable.")]
    public async Task<SessionInfo> Create(
        [Description("Path to the executable.")] string executable,
        [Description("Command-line arguments.")] string? arguments = null,
        [Description("Working directory.")] string? workingDirectory = null,
        [Description("Stop at program entry.")] bool stopAtEntry = true)
        => await session.CreateAsync(executable, arguments, workingDirectory, stopAtEntry);

    [McpServerTool, Description("Attach to a running process by PID.")]
    public async Task<SessionInfo> Attach(
        [Description("Process ID.")] int pid)
        => await session.AttachAsync(pid);

    [McpServerTool, Description("Load a core dump file for post-mortem analysis.")]
    public async Task<SessionInfo> LoadDump(
        [Description("Path to the core dump.")] string path)
        => await session.LoadDumpAsync(path);

    [McpServerTool, Description("Detach from the debugged process, leaving it running.")]
    public async Task<string> Detach()
        => await session.DetachAsync();

    [McpServerTool, Description("Terminate the debugged process and clean up the session.")]
    public async Task<string> Terminate()
        => await session.TerminateAsync();

    [McpServerTool, Description("Query the current debug session status.")]
    public async Task<SessionStatus> Status()
        => await session.StatusAsync();
}
