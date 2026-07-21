# Changelog

## 0.8.1 — 2026-07-21

### GdbMiBridge.Mcp (MCP Server)

- **Capture granularity** — configurable control over what state is captured on breakpoint hits
  - `CaptureGranularity` record: `RegisterPreset` (None/Basic/Full), `CallStack` (bool), `Variables` (bool)
  - Session-wide defaults via `set_default_capture_granularity` / `get_default_capture_granularity` MCP tools
  - Per-breakpoint override on `set_breakpoint`, `set_hardware_breakpoint`, and `capture_state`
  - Default: variables only (minimal overhead)
- **Register improvements** — `get_registers` now returns human-readable names (`"rax"` not `"0"`), new `preset` param (`"full"`/`"basic"`), per-architecture basic register sets (X86/X64/ARM/ARM64/Mips)
- **`get_local_variables`** now includes function arguments (uses `-stack-list-variables` instead of `-stack-list-locals`)
- **`capture_state`** accepts `registers`/`call_stack`/`variables` params for on-demand granularity
- 2 new MCP tools: `set_default_capture_granularity`, `get_default_capture_granularity` (34 tools total)

### GdbMiClient (Library)

- `GdbCommandFactory.StackListVariables` — existing method, now used by `HandleGetLocalVariables`
- `DataListRegisterNames()` — previously unused, now eagerly called at session start for name resolution

### Testing

- 40 unit tests (CaptureGranularity, RegisterSets, ToolHelpers, BreakpointManager, CapturesManager)
- 22 E2E tests including 7 Theory test cases covering all granularity combinations

## 0.8.0 — 2026-07-19

First public release.

### GdbMiClient (Library)

- MI protocol engine: command factory, async transport abstraction (`ITransport`, `LocalTransport`)
- Result parser (`MIResultParser`) — handles MI output records, async/sync notifications, and stream records
- `GdbCommandFactory` — type-safe command construction for all GDB/MI commands
- `GdbMiChannel` — orchestration layer with single-consumer-thread design via `Channel<SessionOperation>`
- Model types: `StopEvent`, `FrameInfo`, `TargetArchitecture`, and more
- Cross-architecture extension points (`TargetArchitecture` with X86/X64/ARM/ARM64/Mips detection)

### GdbMiBridge.Mcp (MCP Server)

- 32 MCP debugging tools for AI agents via Model Context Protocol
- **Session Management**: `create`, `attach`, `load_dump`, `detach`, `terminate`, `status`
- **Execution Control**: `go`, `step_into`, `step_over`, `step_out`, `go_to`
- **Breakpoints**: `set_breakpoint` (function/file:line/address with conditions), `remove_breakpoint`, `enable_breakpoint`, `disable_breakpoint`, `list_breakpoints`
- **Hardware Watchpoints**: `set_hardware_breakpoint` with write/read/access monitoring
- **Program State**: `get_reg`, `get_pc`, `read_memory`, `get_stack`, `list_threads`, `get_locals`, `capture_state`, `get_captures`, `clear_captures`
- **Symbols & Disassembly**: `resolve_symbol`, `address_to_symbol`, `find_symbols`, `disassemble`, `list_modules`
- **Raw**: `raw_gdb` for arbitrary GDB commands
- Auto-capture system: registers, stack, and locals captured on breakpoints and watchpoints
- Breakpoint manager with go/break decision logic
- Background GDB reader thread (prevents deadlocks on blocking reads)

### Distribution

- Self-contained binaries for `linux-x64`, `linux-arm64`, `win-x64`
- One-line install script: `curl -fsSL https://raw.githubusercontent.com/gang2k-coder/GdbMiBridge/master/install.sh | bash`
- NuGet packages: `GdbMiClient` and `GdbMiBridge.Mcp`
- .NET Global Tool: `dotnet tool install -g GdbMiBridge.Mcp`

### Testing

- 30 unit tests for `GdbMiClient` (MI parser, command factory, target architecture)
- 11 unit tests for `GdbMiBridge.Mcp` (breakpoint manager, captures manager, tool handlers)
- E2E integration tests with a real GDB process (disabled in CI by default)
- CI/CD: GitHub Actions — build, test, pack on every push; Release on version tags
