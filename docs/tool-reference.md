# GdbMiBridge MCP — Tool Reference

> GdbMiBridge.Mcp v0.9.0 | GDB 8.0+ | .NET 10.0

---

## Architecture

```
MCP Client (AI Agent)
    │  stdio / JSON-RPC
    ▼
GdbMiBridge.Mcp (MCP Server)
    │
    ├─ SessionTools       — lifecycle
    ├─ ExecutionTools     — run control
    ├─ BreakpointTools    — breakpoints & watchpoints
    ├─ StateTools         — registers, memory, stack, captures
    ├─ SymbolTools        — symbols, disassembly, modules
    └─ RawTools           — raw GDB commands
    │
    ▼
GdbSession              — single consumer thread
    │  Channel<SessionOperation>
    ▼
GdbMiClient             — MI protocol engine
    │  GdbCommandFactory, MIResultParser, LocalTransport
    ▼
GDB process (stdin/stdout)
```

**Key design decisions:**

| Decision | Rationale |
|----------|-----------|
| Single-threaded consumer (`Channel<SessionOperation>`) | All GDB I/O serialized on one thread — no locks needed |
| Background ReadLoop | Prevents deadlock: GDB can emit notifications at any time while MCP tools wait for responses |
| Auto-capture on breakpoints | Registers, stack, and locals captured synchronously at the moment of the stop — avoids "state changed by the time you query" |
| Go-action vs break-action | Go-action doesn't stop the target — ideal for high-frequency breakpoints (loop tracing); break-action stops for interactive debugging |
| MI protocol (not CLI parsing) | Structured async/sync output records — no fragile text scraping |

---

## 1. Session Management

### `create`

Launch an executable for debugging.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `executable` | string | **required** | Path to the executable |
| `arguments` | string? | null | Command-line arguments |
| `workingDirectory` | string? | null | Working directory |
| `stopAtEntry` | bool | true | Stop at the program entry point |

**Returns:** `SessionInfo { Type, ProcessId, ExitCode }`

```
create executable="/home/user/test_target" arguments="arg1 arg2" stopAtEntry=true
```

### `attach`

Attach to a running process by PID.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `pid` | int | **required** | Process ID |

**Returns:** `SessionInfo`

```
attach pid=12345
```

### `load_dump`

Load a core dump for post-mortem analysis.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `path` | string | **required** | Path to the core dump file |

**Returns:** `SessionInfo`

```
load_dump path="/tmp/core.12345"
```

### `detach`

Detach from the debugged process, leaving it running.

```
detach
```

### `terminate`

Terminate the debugged process and clean up the session.

```
terminate
```

### `status`

Query the current debug session status.

**Returns:** `SessionStatus { State }` — `"Stopped"`, `"Running"`, or `"Exited"`

```
status
```

---

## 2. Execution Control

### `go`

Continue execution until the next stop event or timeout.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `timeoutMs` | int | 0 | Timeout in milliseconds (0 = no timeout) |

**Returns:** stop reason string:
- `"breakpoint-hit"` — software or hardware breakpoint
- `"watchpoint-trigger"` — data watchpoint
- `"exited-normally"` — process terminated with exit code 0
- `"end-stepping-range"` — step completed
- `"function-finished"` — step-out completed
- `"signal-received"` — signal (e.g. SIGINT)
- `"timeout"` — timeout elapsed

```
go timeoutMs=30000
```

### `step_into`

Single-step into the next instruction (enters function calls). Returns stop reason (see `go`).

```
step_into
```

### `step_over`

Single-step over the current line (skips function calls). Returns stop reason (see `go`).

```
step_over
```

### `step_out`

Execute until the current function returns. Returns stop reason (see `go`).

```
step_out
```

### `go_to`

Run to a specified location using a temporary breakpoint (auto-removed on arrival).

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `location` | string | **required** | Function name, `file:line`, or address |

Returns stop reason (see `go`).

```
go_to location="main"
go_to location="0x555555555149"
go_to location="test.c:47"
```

---

## 3. Breakpoints

### `set_breakpoint`

Set a software breakpoint. Location can be a function name, `file:line`, or address (prefix addresses with `*`).

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `location` | string | **required** | Function name, `file:line`, or `*`-prefixed address |
| `capture` | bool | true | Auto-capture state when hit. When false, nothing is captured. When true, the three granularity params below control **what** is captured |
| `action` | string | `"break"` | `"break"` = stop and wait; `"go"` = auto-continue after capture |
| `condition` | string? | null | GDB condition expression, e.g. `"i > 5"`, `"argc == 0"` |
| `capture_registers` | string | `"none"` | Which registers to capture: `"none"` (fastest, skip registers), `"basic"` (common GPRs only, ~16 on x64), or `"full"` (all registers including SIMD/vector) |
| `capture_call_stack` | bool | false | Whether to capture the call stack (backtrace) when hit |
| `capture_variables` | bool | true | Whether to capture local variables and function arguments when hit |

**Returns:** `BreakpointConfig { BpNumber, Location, Capture, Action, Condition, Enabled, Granularity }`

```
# Function breakpoint, stop on hit (variables only by default)
set_breakpoint location="add" capture=true action="break"

# Full capture — registers, stack, and variables
set_breakpoint location="main" capture=true capture_registers="full" capture_call_stack=true capture_variables=true

# Lightweight — variables only (default)
set_breakpoint location="loop_body" action="go"

# Conditional breakpoint — only fires when i == 7, full capture
set_breakpoint location="loop_body" condition="i == 7" action="break" capture_registers="full" capture_call_stack=true

# File:line breakpoint
set_breakpoint location="test.c:42" action="break"

# Address breakpoint
set_breakpoint location="*0x555555555149" action="break"

# Go-action with basic registers only — auto-continues, accumulates minimal captures
set_breakpoint location="loop_body" capture=true action="go" capture_registers="basic"
```

### `remove_breakpoint`

Remove a breakpoint by its `BpNumber`.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `id` | string | **required** | Breakpoint ID (BpNumber) |

**Returns:** `true` if removed, `false` if not found.

```
remove_breakpoint id="1"
```

### `enable_breakpoint`

Enable a previously disabled breakpoint.

```
enable_breakpoint id="1"
```

### `disable_breakpoint`

Disable a breakpoint without removing it. It won't fire until re-enabled.

```
disable_breakpoint id="1"
```

### `list_breakpoints`

List all breakpoints in the current session.

**Returns:** `BreakpointConfig[]`

```
list_breakpoints
```

---

## 4. Hardware Watchpoints

### `set_hardware_breakpoint`

Set a hardware data breakpoint (watchpoint) that triggers when memory is read, written, or accessed.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | string | **required** | Hex address, e.g. `"0x601040"`. Use `resolve_symbol` to find a variable's address |
| `access` | string | `"write"` | Access type: `"write"`, `"read"`, or `"access"` |
| `size` | int | 4 | Watch size in bytes: 1, 2, 4, or 8 |
| `capture` | bool | true | Auto-capture state when triggered. Same semantics as `set_breakpoint` — granularity params below control what is captured |
| `capture_registers` | string | `"none"` | Which registers to capture: `"none"`, `"basic"`, or `"full"` |
| `capture_call_stack` | bool | false | Whether to capture the call stack on trigger |
| `capture_variables` | bool | true | Whether to capture local variables and function arguments on trigger |

**Returns:** `BreakpointConfig`

**Limitations:** Hardware watchpoints are limited by CPU debug registers (typically 4).

```
# Resolve the variable address first
resolve_symbol name="g_counter"
# → { "name": "g_counter", "address": "0x555555558014" }

# Then set a write watchpoint (variables only by default)
set_hardware_breakpoint address="0x555555558014" access="write" size=4

# Full capture on watchpoint trigger
set_hardware_breakpoint address="0x555555558014" access="write" size=4 \
    capture_registers="full" capture_call_stack=true
```

---

## 5. Program State

### `get_registers`

Get CPU register values with human-readable names (e.g. `"rax"` on x64, `"x0"` on ARM64).

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `preset` | string | `"full"` | `"full"` = all registers including SIMD/vector (~176 on x64); `"basic"` = common GPRs and control registers only (~16 on x64) |

**Returns:** `Dictionary<string, string>` — register name → hex value

```
# All registers
get_registers

# Basic/common registers only (faster, less token usage)
get_registers preset="basic"
```

### `get_program_counter`

Get the program counter with symbol and instruction.

**Returns:** `ProgramCounterInfo { Address, Symbol, Instruction }`

```
get_program_counter
```

### `read_memory`

Read raw memory at the given address.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | string | **required** | Hex address, e.g. `"0x601040"` |
| `size` | int | 64 | Number of bytes to read |

**Returns:** `MemoryData { Address, Size, Hex, Bytes, Ascii }`

```
read_memory address="0x601040" size=128
```

### `get_call_stack`

Get the current call stack.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `maxFrames` | int | 20 | Maximum number of frames to return |

**Returns:** `FrameInfo[] { Level, Address, FunctionName, File, FullName, Line }`

```
get_call_stack maxFrames=50
```

### `list_threads`

List all threads in the debugged process.

**Returns:** `ThreadInfo[] { ThreadId, IsCurrent }`

```
list_threads
```

### `get_local_variables`

Get **local variables and function arguments** for a stack frame. Frame 0 is the current/innermost frame.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `frameIndex` | int | 0 | Frame index (0 = current frame). Higher numbers go up the call stack |

**Returns:** `VariableInfo[] { Name, Type, Value }`

```
# Current frame
get_local_variables frameIndex=0
```

---

## 6. Auto-Capture

Auto-capture is the most powerful feature of GdbMiBridge. When a breakpoint or watchpoint is hit with `capture=true`, the session captures state — stored as a snapshot. **What** is captured is controlled by capture granularity.

### Capture Granularity

Each breakpoint and the session itself can be configured with three independent toggles:

| Setting | Options | Default | Effect |
|---------|---------|---------|--------|
| Registers | `"none"`, `"basic"`, `"full"` | `"none"` | Whether and which registers to capture. `"basic"` = common GPRs (~16 regs), `"full"` = all registers (~176 on x64) |
| Call Stack | bool | `false` | Whether to capture the backtrace (list of stack frames) |
| Variables | bool | `true` | Whether to capture local variables and function arguments |

**Resolution order:** per-breakpoint granularity → session default (`set_default_capture_granularity`) → built-in default (variables only).

### `set_default_capture_granularity`

Configure the **session-wide** capture defaults. Applied to all breakpoints that do not specify their own granularity override. Call once at session start to set your debugging strategy.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `registers` | string | `"none"` | `"none"` (fastest), `"basic"` (common GPRs), or `"full"` (all registers including SIMD) |
| `call_stack` | bool | false | Whether to capture call stack by default |
| `variables` | bool | true | Whether to capture variables by default |

```
# Set session to always capture everything
set_default_capture_granularity registers="full" call_stack=true variables=true

# Set session for lightweight tracing (variables only)
set_default_capture_granularity registers="none" call_stack=false variables=true
```

### `get_default_capture_granularity`

Query the current session-wide capture defaults.

**Returns:** `CaptureGranularity { Registers, CallStack, Variables }` — `Registers` is `"none"`, `"basic"`, or `"full"`

```
get_default_capture_granularity
```

### Workflow

```
set_default_capture_granularity registers="full" call_stack=true variables=true
set_breakpoint location="func" capture=true action="go"
    → go() runs the target
    → breakpoint fires, state captured per granularity on the consumer thread
    → go-action auto-continues; break-action waits
    → snapshot stored in the captures list
    → user calls get_captures to retrieve all snapshots
```

### `capture_state`

Manually capture the current program state. Target must be stopped. Uses session defaults unless overridden.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `registers` | string | `"none"` | `"none"`, `"basic"`, or `"full"` |
| `call_stack` | bool | false | Whether to include the call stack |
| `variables` | bool | true | Whether to include local variables and function arguments |

**Returns:** `CaptureResult { BreakpointNumber, BreakpointLocation, Registers, ProgramCounter, CallStack, Memory, LocalVariables, Timestamp }`

```
# Default granularity (variables only)
capture_state

# Full capture on demand
capture_state registers="full" call_stack=true variables=true
```

### `get_captures`

Get all accumulated capture snapshots since the last `clear_captures`.

**Returns:** `CaptureResult[]`

```
get_captures
```

### `clear_captures`

Clear all accumulated capture snapshots. Call this before starting a new trace.

```
clear_captures
```

### Typical loop-tracing pattern

```
# 1. Set session defaults
set_default_capture_granularity registers="none" call_stack=false variables=true

# 2. Clear any stale captures
clear_captures

# 3. Set a go-action breakpoint in the loop body (variables only — fast)
set_breakpoint location="loop_body" capture=true action="go"

# 4. Set a break-action breakpoint after the loop (full capture for the final stop)
set_breakpoint location="after_loop" capture=true action="break" \
    capture_registers="full" capture_call_stack=true

# 5. Run
go timeoutMs=30000

# 6. Retrieve all captures — loop captures are compact, final capture is detailed
get_captures
```

---

## 7. Symbols & Disassembly

### `resolve_symbol`

Resolve a symbol name to its address.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name` | string | **required** | Symbol name (e.g. `"main"`, `"g_counter"`) |

**Returns:** `SymbolInfo { Name, Address }`

```
resolve_symbol name="main"
resolve_symbol name="g_counter"
```

### `address_to_symbol`

Look up the symbol name for an address.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | string | **required** | Hex address |

**Returns:** `SymbolInfo { Name, Address }`

```
address_to_symbol address="0x555555555149"
```

### `find_symbols`

Search for symbols matching a pattern (supports `*` wildcards).

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `pattern` | string | **required** | Pattern with `*` wildcards |

**Returns:** `SymbolInfo[]`

```
find_symbols pattern="*main*"
find_symbols pattern="*counter*"
```

### `disassemble`

Disassemble instructions at an address or symbol.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `address` | string | **required** | Address or symbol (e.g. `"main"` or `"0x555555555149"`) |
| `count` | int | 10 | Approximate number of instructions to return |

**Returns:** `DisassemblyLine[] { Address, Opcode, Instruction }`

```
disassemble address="main" count=20
disassemble address="0x555555555149" count=10
```

### `list_modules`

List all loaded shared libraries.

**Returns:** `ModuleInfo[] { Name, BaseAddress, Size }`

```
list_modules
```

---

## 8. Raw GDB Commands

### `raw_gdb`

Execute an arbitrary GDB MI or CLI command directly.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `command` | string | **required** | The GDB command to execute |

**Returns:** GDB output as a string.

```
raw_gdb command="-data-list-register-names"
raw_gdb command="info registers"
raw_gdb command="x/16x 0x601040"
```

---

## 9. Complete Tool Index

| # | Tool | Category | Parameters |
|---|------|----------|------------|
| 1 | `create` | Session | `executable`, `arguments?`, `workingDirectory?`, `stopAtEntry` |
| 2 | `attach` | Session | `pid` |
| 3 | `load_dump` | Session | `path` |
| 4 | `detach` | Session | — |
| 5 | `terminate` | Session | — |
| 6 | `status` | Session | — |
| 7 | `go` | Execution | `timeoutMs` |
| 8 | `step_into` | Execution | — |
| 9 | `step_over` | Execution | — |
| 10 | `step_out` | Execution | — |
| 11 | `go_to` | Execution | `location` |
| 12 | `set_breakpoint` | Breakpoint | `location`, `capture`, `action`, `condition?`, `capture_registers`, `capture_call_stack`, `capture_variables` |
| 13 | `remove_breakpoint` | Breakpoint | `id` |
| 14 | `enable_breakpoint` | Breakpoint | `id` |
| 15 | `disable_breakpoint` | Breakpoint | `id` |
| 16 | `list_breakpoints` | Breakpoint | — |
| 17 | `set_hardware_breakpoint` | Breakpoint | `address`, `access`, `size`, `capture`, `capture_registers`, `capture_call_stack`, `capture_variables` |
| 18 | `get_registers` | State | `preset` |
| 19 | `get_program_counter` | State | — |
| 20 | `read_memory` | State | `address`, `size` |
| 21 | `get_call_stack` | State | `maxFrames` |
| 22 | `list_threads` | State | — |
| 23 | `get_local_variables` | State | `frameIndex` |
| 24 | `capture_state` | State | `registers`, `call_stack`, `variables` |
| 25 | `get_captures` | State | — |
| 26 | `clear_captures` | State | — |
| 27 | `set_default_capture_granularity` | State | `registers`, `call_stack`, `variables` |
| 28 | `get_default_capture_granularity` | State | — |
| 29 | `resolve_symbol` | Symbol | `name` |
| 30 | `address_to_symbol` | Symbol | `address` |
| 31 | `find_symbols` | Symbol | `pattern` |
| 32 | `disassemble` | Symbol | `address`, `count` |
| 33 | `list_modules` | Symbol | — |
| 34 | `raw_gdb` | Raw | `command` |

---

## Return Types

```
SessionInfo      { Type, ProcessId, ExitCode }
SessionStatus    { State }
BreakpointConfig { BpNumber, Location, Capture, Action, Condition, Enabled, Granularity }
CaptureResult    { BreakpointNumber, BreakpointLocation, Registers,
                   ProgramCounter, CallStack, Memory, LocalVariables, Timestamp }
CaptureGranularity { Registers ("none"|"basic"|"full"), CallStack, Variables }
MemoryData       { Address, Size, Hex, Bytes, Ascii }
ThreadInfo       { ThreadId, IsCurrent }
VariableInfo     { Name, Type, Value }
ProgramCounterInfo { Address, Symbol, Instruction }
SymbolInfo       { Name, Address }
DisassemblyLine  { Address, Opcode, Instruction }
ModuleInfo       { Name, BaseAddress, Size }
FrameInfo        { Level, Address, FunctionName, File, FullName, Line }
```
