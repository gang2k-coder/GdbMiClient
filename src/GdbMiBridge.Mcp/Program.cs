using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using GdbMiBridge.Mcp;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<GdbSession>();
builder.Services.AddSingleton<SessionTools>();
builder.Services.AddSingleton<ExecutionTools>();
builder.Services.AddSingleton<BreakpointTools>();
builder.Services.AddSingleton<StateTools>();
builder.Services.AddSingleton<SymbolTools>();
builder.Services.AddSingleton<RawTools>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
