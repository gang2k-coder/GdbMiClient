using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GdbMiBridge.Mcp;

[McpServerToolType]
public class SymbolTools(GdbSession session)
{
    [McpServerTool, Description("Resolve a symbol name to its address.")]
    public async Task<SymbolInfo> ResolveSymbol(
        [Description("Symbol name.")] string name)
        => await session.ResolveSymbolAsync(name);

    [McpServerTool, Description("Look up the symbol name for an address.")]
    public async Task<SymbolInfo> AddressToSymbol(
        [Description("Hex address.")] string address)
        => await session.AddressToSymbolAsync(address);

    [McpServerTool, Description("Search for symbols matching a pattern.")]
    public async Task<List<SymbolInfo>> FindSymbols(
        [Description("Pattern with * wildcards.")] string pattern)
        => await session.FindSymbolsAsync(pattern);

    [McpServerTool, Description("Disassemble instructions at an address.")]
    public async Task<List<DisassemblyLine>> Disassemble(
        [Description("Address or symbol.")] string address,
        [Description("Number of instructions.")] int count = 10)
        => await session.DisassembleAsync(address, count);

    [McpServerTool, Description("List loaded shared libraries.")]
    public async Task<List<ModuleInfo>> ListModules()
        => await session.ListModulesAsync();
}
