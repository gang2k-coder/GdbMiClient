# WinDbg Backend 功能与用法参考

> 适用版本：DebugBridge v2.4+ | 最后更新：2026-07-03

---

## 目录

1. [架构概述](#1-架构概述)
2. [会话生命周期](#2-会话生命周期)
3. [执行控制](#3-执行控制)
4. [断点管理](#4-断点管理)
5. [寄存器与内存](#5-寄存器与内存)
6. [调用栈与线程](#6-调用栈与线程)
7. [符号与反汇编](#7-符号与反汇编)
8. [局部变量](#8-局部变量)
9. [自动捕获 (Auto-Capture)](#9-自动捕获-auto-capture)
10. [WinDbg 专属工具](#10-windbg-专属工具)
11. [原始命令 (Escape Hatch)](#11-原始命令-escape-hatch)
12. [硬件断点](#12-硬件断点)
13. [完整工具列表](#13-完整工具列表)

---

## 1. 架构概述

WinDbg 后端通过 **DbgEng COM API**（`dbgeng.dll`）与调试目标交互。整个架构的核心约束是 **DbgEng 线程亲和性**：同一个 debug client 的所有操作必须在同一线程上执行。

```
MCP Client (AI/Agent)
    │
    ▼
MCP Server (JSON-RPC)
    │
    ▼
WinDbgBackend          ← IDebugBackend 实现
    │
    ▼
DbgEngSession          ← 封装调试操作（创建进程、下断点、读写内存等）
    │
    ▼
DbgEngDispatcher      ← 单线程调度器（Channel + Worker Thread）
    │
    ▼
DbgEng COM (dbgeng.dll)  ← Windows 调试引擎
```

**关键组件：**

| 组件 | 文件 | 职责 |
|------|------|------|
| `WinDbgBackend` | `WinDbgBackend.cs` | `IDebugBackend` 实现，业务逻辑层 |
| `DbgEngSession` | `DbgEngSession.cs` | DbgEng COM 的封装，执行具体调试操作 |
| `DbgEngDispatcher` | `DbgEngDispatcher.cs` | 单线程调度器，确保所有 COM 调用在同一线程 |
| `DebugEventCallback` | `DebugEventCallback.cs` | COM 事件回调，处理断点/异常/进程退出 |
| `DebugOutputCallback` | `DebugOutputCallback.cs` | 捕获 DbgEng 输出到 `ILogger`，防止污染 MCP 通道 |

---

## 2. 会话生命周期

### 2.1 创建会话 — `create`

启动一个可执行文件并开始调试。

```
create executable=<path> [arguments=<args>] [workingDirectory=<dir>] [stop_at_entry=true] [backend=auto]
```

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `executable` | string | **必填** | 可执行文件路径 |
| `arguments` | string? | null | 命令行参数 |
| `workingDirectory` | string? | null | 工作目录 |
| `stop_at_entry` | bool | true | 是否停在入口点 |
| `backend` | string | "auto" | 后端选择：`auto`/`windbg`/`gdb` |

**返回：** `SessionInfo` — `{ type: "create", processId, exitCode, backendName }`

**示例：**
```
create executable="C:\test\test_target.exe" arguments="arg1 arg2" stop_at_entry=true
```

### 2.2 附加到进程 — `attach`

```
attach pid=<processId> [backend=auto]
```

### 2.3 加载 Dump 文件 — `load_dump`

```
load_dump path=<dumpFilePath> [backend=auto]
```

加载后可用 `analyze_dump` 执行 `!analyze -v`。

### 2.4 分离 — `detach`

调试器分离，目标进程继续运行。

```
detach
```

### 2.5 终止 — `terminate`

终止被调试进程并清理会话。

```
terminate
```

### 2.6 状态查询 — `status`

```
status
```

**返回：** `{ state: "idle"|"starting"|"break"|"running"|"terminated", backend: "WinDbg" }`

### 2.7 后端发现 — `list_backends`

```
list_backends
```

**返回：** `{ backends: [...], current_backend, default_backend }`

---

## 3. 执行控制

### 3.1 继续执行 — `go`

```
go [timeoutMs=10000]
```

继续执行直到遇到下一个调试事件（断点、异常、进程退出）或超时。

**返回：** 停止原因字符串 — `"breakpoint_hit"` / `"step_complete"` / `"go_to_reached"` / `"process_exited"` / `"exception"` / `"timeout"`

### 3.2 单步进入 — `step_into`

```
step_into
```

执行当前指令，如果遇到函数调用则进入函数内。

### 3.3 单步跳过 — `step_over`

```
step_over
```

执行当前指令，跳过函数调用（不进入函数内部）。

### 3.4 单步返回 — `step_out`

```
step_out
```

执行直到当前函数返回。

### 3.5 运行到指定位置 — `go_to`

```
go_to location=<functionName|address>
```

使用一次性断点运行到指定函数或地址，到达后自动清除。不留下永久断点。

**示例：**
```
go_to location="test_target!after_loop"
go_to location="0x40001050"
```

---

## 4. 断点管理

### 4.1 软件断点 — `set_breakpoint`

```
set_breakpoint location=<location> [capture=true] [action="go"] [condition=null]
```

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `location` | string | **必填** | 函数名、`file:line` 或地址 |
| `capture` | bool | true | 命中时自动抓取寄存器/调用栈/内存 |
| `action` | string | "go" | 命中后行为：`"go"`（自动继续 + 累积捕获）或 `"break"`（停止等待） |
| `condition` | string? | null | 条件表达式，如 `"x > 5"`、`"argc == 0"` |

**返回：** `BreakpointInfo` — `{ id, location, address, enabled, hasCondition, captureState, afterHit }`

**示例：**
```
# 在函数入口下断点，自动继续并捕获
set_breakpoint location="test_target!add" capture=true action="go"

# 条件断点
set_breakpoint location="test_target!loop_body" condition="i % 100 == 0" action="break"

# 源码行断点（需要 PDB 私有符号）
set_breakpoint location="test_target.c:42" action="break"
```

### 4.2 移除断点 — `remove_breakpoint`

```
remove_breakpoint id=<breakpointId>
```

### 4.3 启用/禁用断点

```
enable_breakpoint id=<breakpointId>
disable_breakpoint id=<breakpointId>
```

禁用后断点保留但不会触发；启用后恢复。

### 4.4 列出断点 — `list_breakpoints`

```
list_breakpoints
```

**返回：** 所有断点列表，含 ID、位置、地址、启用状态、捕获设置、命中行为、条件表达式。

---

## 5. 寄存器与内存

### 5.1 所有寄存器 — `get_registers`

```
get_registers
```

**返回：** 字典 `{ "rax": "0x...", "rip": "0x...", ... }` — 25 个标准寄存器。

### 5.2 单个寄存器 — `get_reg`

```
get_reg name=<registerName>
```

**示例：**
```
get_reg name="rip"
get_reg name="rax"
```

### 5.3 程序计数器 — `get_program_counter`

```
get_program_counter
```

**返回：** `{ address: "0x...", symbol: "test_target!add", instruction: "mov ..." }`

### 5.4 读取内存 — `read_memory`

```
read_memory address=<hexAddress> [size=64]
```

**返回：** `MemoryData` — `{ address, size, hex, bytes, ascii }`

**示例：**
```
read_memory address="0x401000" size=128
```

### 5.5 读取指针数组 — `ReadPointersAsync`

> 内部方法，通过 pointer dereference 隐式使用。在 `GetLocalVariablesAsync` 中自动解引用简单类型指针。

### 5.6 读取字符串 — `ReadStringAsync`

> 内部方法，支持 ASCII 和 Wide (UTF-16) 字符串。

---

## 6. 调用栈与线程

### 6.1 调用栈 — `get_call_stack`

```
get_call_stack [maxFrames=20]
```

**返回：** `StackFrame[]` — 每帧含 `{ index, returnAddress, functionName }`

底层执行 `kn` 命令，解析帧号（支持 256+ 帧号）、返回地址和函数名。处理 symbol header line 和 column-aligned 格式。

**示例返回：**
```
0  0000000140001000  test_target!add
1  0000000140001050  test_target!main
2  00000001400010a0  kernel32!BaseThreadInitThunk
3  00000001400010b0  ntdll!RtlUserThreadStart
```

### 6.2 线程列表 — `list_threads`

```
list_threads
```

**返回：** `ThreadInfo[]` — `{ threadId, isCurrent }`

---

## 7. 符号与反汇编

### 7.1 解析符号 → 地址 — `resolve_symbol`

```
resolve_symbol name=<symbolName>
```

**示例：**
```
resolve_symbol name="test_target!add"
resolve_symbol name="main"
```

**返回：** `{ name: "test_target!add", address: "0x40001000" }`

### 7.2 地址 → 符号名 — `address_to_symbol`

```
address_to_symbol address=<hexAddress>
```

**返回：** `{ address: "0x40001000", symbol: "test_target!add" }`

### 7.3 搜索符号 — `find_symbols`

```
find_symbols pattern=<pattern>
```

支持通配符 `*` 和模块限定 `module!pattern`。

**示例：**
```
find_symbols pattern="test_target!add*"
find_symbols pattern="kernel32!*Alloc*"
find_symbols pattern="*loop*"
```

### 7.4 反汇编 — `disassemble`

```
disassemble address=<hexAddress|symbolName> [count=10]
```

地址可以是 `0x401000` 或符号名 `test_target!add`。

**返回：** `DisassemblyLine[]` — `{ address, opcode, instruction }`

底层执行 `u <address> L<count>` 命令，自动跳过 symbol header line。

### 7.5 模块列表 — `list_modules`

```
list_modules
```

**返回：** `ModuleInfo[]` — `{ name, baseAddress, size, symbolStatus }`

底层执行 `lm` 命令。

---

## 8. 局部变量

### 8.1 `get_local_variables`

```
get_local_variables [frameIndex=0]
```

底层执行 `dv /t /v` 命令（WinDbg）。

**智能解引用：**
- **简单类型**（int, float, char）：直接显示值
- **简单类型指针**（int*, char*, float*）：自动用 `dp` 解引用，显示指向的值
- **复杂类型**（struct, class, union）：显示地址和类型名，不自动解引用

**值格式化：**
- `0n` 前缀的十进制会自动转换为纯数字（如 `0n42` → `42`）
- 十六进制地址保持原始格式

**示例返回：**
```
Name     Type                  Value
────────────────────────────────────────────
disc     int                   1
from     int                   1
to       int                   2
a        struct Point *        0x0000000060000000
dx       int                   42
dy       int                   7
```

---

## 9. 自动捕获 (Auto-Capture)

### 核心设计

自动捕获是 WinDbg 后端最重要的功能模式。断点命中时可以自动抓取完整的程序状态（寄存器、调用栈、内存、PC），无需额外调用。

### 工作流程

```
set_breakpoint location="func" capture=true action="go"
    → go() 或 step_*() 执行
    → 断点命中，DebugEventCallback 在 COM 线程同步捕获状态
    → 返回 DEBUG_STATUS.GO 自动继续
    → 捕获快照存入 PendingCaptures 队列
    → GoAsync 将快照转存到 WinDbgBackend._captures 列表
    → 用户调用 get_captures 获取所有累积快照
```

### 9.1 获取捕获 — `get_captures`

```
get_captures
```

**返回：** `CaptureResult[]` — 每个快照包含：
```
{
  breakpointId: 1,
  breakpointExpression: "test_target!add",
  registers: { rip: "...", rax: "...", ... },
  programCounter: { address: "...", symbol: "...", instruction: "..." },
  callStack: [ ... ],
  memory: { address: "...", bytes: [...] },
  localVariables: [ ... ]
}
```

### 9.2 清空捕获 — `clear_captures`

```
clear_captures
```

在开始新一轮追踪前清空缓冲区。

### 9.3 手动捕获 — `capture_state`

```
capture_state
```

在当前停止位置手动触发完整状态快照。不依赖断点。

### 典型追踪模式

```
# 1. 清空旧数据
clear_captures

# 2. 在循环体设 go-action 断点（自动捕获 + 自动继续）
set_breakpoint location="test_target!loop_body" capture=true action="go"

# 3. 在循环后设 break-action 断点（停止）
set_breakpoint location="test_target!after_loop" capture=true action="break"

# 4. 运行
go timeoutMs=30000

# 5. 获取所有循环迭代的捕获
get_captures
```

---

## 10. WinDbg 专属工具

### 10.1 设置符号路径 — `set_symbol_path`

```
set_symbol_path path=<symbolPath>
```

**示例：**
```
# 使用 Microsoft 公共符号服务器
set_symbol_path path="srv*C:\Symbols*https://msdl.microsoft.com/download/symbols"

# 添加本地 PDB 目录
set_symbol_path path="srv*C:\Symbols*https://msdl.microsoft.com/download/symbols;C:\MyProject\bin"
```

### 10.2 分析 Dump — `analyze_dump`

```
analyze_dump
```

需要先通过 `load_dump` 加载 dump 文件。执行 `!analyze -v` 并返回：
- Bugcheck 分析
- 异常信息
- 调用栈
- 可能的根因

---

## 11. 原始命令 (Escape Hatch)

### 11.1 `raw_windbg`

```
raw_windbg command=<dbgEngCommand>
```

执行任意 DbgEng 文本命令。支持所有内置命令和扩展命令。

**常用命令示例：**

| 命令 | 说明 |
|------|------|
| `lm` | 列出模块 |
| `r` | 显示寄存器 |
| `bp <addr>` | 设置断点 |
| `dt <type> <addr>` | 显示类型（如 `dt ntdll!_PEB`） |
| `u <addr>` | 反汇编 |
| `dv /t /v` | 显示局部变量 |
| `!analyze -v` | 分析 dump |
| `!heap -a` | 堆分析 |
| `!process 0 0` | 列出所有进程 |
| `!peb` | 显示 PEB |
| `!teb` | 显示 TEB |
| `.reload /f` | 强制重新加载符号 |

### 11.2 `raw_gdb`

```
raw_gdb command=<gdbCommand>
```

执行 GDB/MI 命令。仅 GDB 后端可用。

---

## 12. 硬件断点

### 12.1 `set_hardware_breakpoint`

通过 DbgEng COM API（`AddDataBreakpointAsync`）设置硬件断点，而非 `Execute("ba")` 命令。

```
set_hardware_breakpoint address=<hexAddress> [access="execute"] [size=4] [capture=true]
```

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `address` | string | **必填** | 十六进制地址（如 `0x401000`） |
| `access` | string | "execute" | 访问类型：`execute` / `write` / `readwrite` / `read` |
| `size` | int | 4 | 监控大小（字节）：1, 2, 4, 8 |
| `capture` | bool | true | 命中时自动捕获状态 |

**示例：**
```
# 执行断点
set_hardware_breakpoint address="0x401000" access="execute"

# 写监控（数据断点/watchpoint）
set_hardware_breakpoint address="0x600010" access="write" size=4
```

> **限制：** 硬件断点受 CPU 调试寄存器数量限制（通常 4 个）。GDB 和 WinDbg 后端均支持。

---

## 13. 完整工具列表

### 通用工具（所有后端适用）

| # | 工具名 | 分类 | 功能 |
|---|--------|------|------|
| 1 | `list_backends` | Session | 列出可用后端 |
| 2 | `create` | Session | 创建调试会话 |
| 3 | `attach` | Session | 附加到进程 |
| 4 | `load_dump` | Session | 加载 Dump |
| 5 | `detach` | Session | 分离调试器 |
| 6 | `terminate` | Session | 终止进程 |
| 7 | `status` | Session | 会话状态 |
| 8 | `go` | Execution | 继续执行 |
| 9 | `step_into` | Execution | 单步进入 |
| 10 | `step_over` | Execution | 单步跳过 |
| 11 | `step_out` | Execution | 单步返回 |
| 12 | `go_to` | Execution | 运行到指定位置 |
| 13 | `set_breakpoint` | Breakpoint | 设置软件断点 |
| 14 | `set_hardware_breakpoint` | Breakpoint | 设置硬件断点 |
| 15 | `remove_breakpoint` | Breakpoint | 移除断点 |
| 16 | `enable_breakpoint` | Breakpoint | 启用断点 |
| 17 | `disable_breakpoint` | Breakpoint | 禁用断点 |
| 18 | `list_breakpoints` | Breakpoint | 列出断点 |
| 19 | `get_registers` | State | 所有寄存器 |
| 20 | `get_reg` | State | 单个寄存器 |
| 21 | `read_memory` | State | 读取内存 |
| 22 | `get_call_stack` | State | 调用栈 |
| 23 | `list_threads` | State | 线程列表 |
| 24 | `get_captures` | State | 获取捕获快照 |
| 25 | `clear_captures` | State | 清空捕获 |
| 26 | `capture_state` | State | 手动捕获快照 |
| 27 | `get_program_counter` | State | 程序计数器 |
| 28 | `get_local_variables` | State | 局部变量 |
| 29 | `resolve_symbol` | Symbol | 符号→地址 |
| 30 | `address_to_symbol` | Symbol | 地址→符号 |
| 31 | `find_symbols` | Symbol | 搜索符号 |
| 32 | `disassemble` | Symbol | 反汇编 |
| 33 | `list_modules` | Symbol | 模块列表 |

### WinDbg 专属工具

| # | 工具名 | 功能 |
|---|--------|------|
| 34 | `set_symbol_path` | 设置符号服务器路径 |
| 35 | `analyze_dump` | `!analyze -v` dump 分析 |
| 36 | `raw_windbg` | 执行任意 DbgEng 命令 |

---

## 关键设计决策

| 决策 | 原因 |
|------|------|
| **单线程调度器 (DbgEngDispatcher)** | DbgEng COM 要求同一 client 所有操作在同一线程 |
| **自动捕获 (Auto-Capture)** | 断点命中时同步抓状态，避免"停在断点时状态已变"的问题 |
| **Go-Action vs Break-Action** | Go-action 不停止目标，性能好，适合高频断点（循环追踪）；Break-action 停止，适合交互式调试 |
| **硬件断点用 COM API 而非 Execute("ba")** | COM API 直接、可靠，不依赖文本输出解析 |
| **stdout 保护 (DebugOutputCallback)** | DbgEng 所有输出路由到 ILogger，绝不污染 MCP JSON-RPC |
| **文件行断点需要 .lines -e** | WinDbg 默认不加载行号信息，Session 创建后自动执行 |
