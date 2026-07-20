# GdbMiClient

[![CI](https://github.com/gang2k-coder/GdbMiClient/actions/workflows/ci.yml/badge.svg)](https://github.com/gang2k-coder/GdbMiClient/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/GdbMiClient)](https://www.nuget.org/packages/GdbMiClient/)
[![NuGet](https://img.shields.io/nuget/v/GdbMiBridge.Mcp)](https://www.nuget.org/packages/GdbMiBridge.Mcp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

GDB Machine Interface (MI) client for .NET, with a full-featured MCP server that exposes 34 debugging tools to AI agents.

## Packages

| Package | Type | Description |
|---------|------|-------------|
| **GdbMiClient** | Library | Async MI protocol engine — command factory, result parser, transport abstraction |
| **GdbMiBridge.Mcp** | MCP Server | Stdio MCP server — connects AI agents to GDB for C/C++ debugging |

## Installation

### Option 1: .NET Global Tool

```bash
dotnet tool install -g GdbMiBridge.Mcp
```

Requires [.NET SDK 10.0+](https://dotnet.microsoft.com/download).

### Option 2: Self-Contained Binary

```bash
curl -fsSL https://raw.githubusercontent.com/gang2k-coder/GdbMiClient/master/install.sh | bash
```

No dependencies — the binary bundles the .NET runtime. Supports `linux-x64` and `linux-arm64`.

Or download directly from [GitHub Releases](https://github.com/gang2k-coder/GdbMiClient/releases).

## Quick Start

1. Install via either method above
2. Configure your MCP client:

**Claude Code** (`.mcp.json`):
```json
{
  "mcpServers": {
    "gdb-debug": {
      "type": "stdio",
      "command": "GdbMiBridge"
    }
  }
}
```

**VS Code** (`mcp.json`):
```json
{
  "servers": {
    "gdb-debug": {
      "type": "stdio",
      "command": "GdbMiBridge"
    }
  }
}
```

3. The AI agent can now debug C/C++ programs via GDB.

## MCP Tools (34 total)

### Session Management
| Tool | Description |
|------|-------------|
| `create` | Launch an executable for debugging |
| `attach` | Attach to a running process by PID |
| `load_dump` | Load a core dump for post-mortem analysis |
| `detach` | Detach from the debugged process |
| `terminate` | Terminate the debugged process |
| `status` | Query current session status |

### Execution Control
| Tool | Description |
|------|-------------|
| `go` | Continue execution until stop or timeout |
| `step_into` | Single-step into the next instruction |
| `step_over` | Single-step over the current line |
| `step_out` | Execute until current function returns |
| `go_to` | Run to a specified location via temporary breakpoint |

### Breakpoints
| Tool | Description |
|------|-------------|
| `set_breakpoint` | Set breakpoint (function, file:line, or address) with capture/condition |
| `remove_breakpoint` | Remove a breakpoint by ID |
| `enable_breakpoint` | Enable a breakpoint |
| `disable_breakpoint` | Disable a breakpoint without removing it |
| `list_breakpoints` | List all breakpoints in current session |
| `set_hardware_breakpoint` | Set a hardware data breakpoint (watchpoint) on memory access |

### Program State
| Tool | Description |
|------|-------------|
| `get_registers` | Get CPU registers with human-readable names (preset: full/basic) |
| `get_program_counter` | Get program counter with symbol info |
| `read_memory` | Read memory at an address |
| `get_call_stack` | Get the call stack |
| `list_threads` | List all threads |
| `get_local_variables` | Get local variables and function arguments for a frame |
| `capture_state` | Manually capture state with configurable granularity |
| `get_captures` | Get accumulated capture snapshots |
| `clear_captures` | Clear accumulated capture snapshots |
| `set_default_capture_granularity` | Set session-wide capture defaults (registers/stack/vars) |
| `get_default_capture_granularity` | Query current capture granularity settings |

### Symbols & Disassembly
| Tool | Description |
|------|-------------|
| `resolve_symbol` | Resolve symbol name to address |
| `address_to_symbol` | Look up symbol name for an address |
| `find_symbols` | Search symbols by pattern |
| `disassemble` | Disassemble instructions at an address |
| `list_modules` | List loaded shared libraries |

### Raw
| Tool | Description |
|------|-------------|
| `raw_gdb` | Execute an arbitrary GDB command |

## Requirements

- **GDB** 8.0 or later
- **.NET SDK 10.0** (only for the Global Tool install method)

## Platform Compatibility

This project has been tested on **x86-64 (amd64)** Linux only. While most GDB/MI commands use the same format across architectures, running on other platforms may encounter issues due to differences in:

- **Address width** — 32-bit vs 64-bit address formatting in MI output
- **Register names** — register sets differ between x86, ARM, RISC-V, etc.
- **Instruction set** — disassembly output varies by architecture

If you run into issues on x86, ARM, or other architectures, contributions and bug reports are welcome.

## Architecture

```
MCP Client (AI Agent)  <--stdio/JSON-RPC-->  GdbMiBridge.Mcp
                                                   |
                                           GdbSession (single consumer thread)
                                                   |
                                            GdbMiClient (MI protocol engine)
                                                   |
                                               GDB process
```

All GDB I/O runs on a single consumer thread via `Channel<SessionOperation>`. The MCP tool handlers are invoked on the MCP thread pool and enqueue operations. This design eliminates locks — the breakpoint manager, capture manager, and MI protocol client all share the same thread.

## Building from Source

```bash
git clone https://github.com/gang2k-coder/GdbMiClient.git
cd GdbMiClient
dotnet build
dotnet test
```

To run the MCP server locally:
```bash
dotnet run --project src/GdbMiBridge.Mcp
```

## License

MIT — see [LICENSE](LICENSE) for details.

## Acknowledgments

The MI protocol engine (`GdbMiClient` library) is adapted from [Microsoft/MIEngine](https://github.com/microsoft/MIEngine), the engine that powers C/C++ debugging in Visual Studio and VS Code. We're grateful to the MIEngine authors for their excellent work.
