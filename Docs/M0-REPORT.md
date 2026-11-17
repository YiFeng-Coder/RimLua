# RimLua M0 可行性报告

- 日期：2026-11-16
- 结论：**通过**（游戏内 27 项冒烟全部通过，0 失败）
- 版本：RimLua 0.1.0-m0
- 平台：RimWorld 1.7.5011 rev24 / Waylith / .NET 10.0.15-waylithclr.dev
- 依据：[RimLua_实现计划_v0.1.md](../RimLua_实现计划_v0.1.md) §9.1 M0

---

## 1. M0 目标回顾

| 目标 | 结果 |
| --- | --- |
| 搭出 net10.0 工程骨架，能加载进游戏 | 通过 |
| 游戏内加载 MoonSharp 并执行脚本 | 通过（MoonSharp 3.0.0.0） |
| 硬沙箱：io/os/require 等不可用 | 通过（逐项 nil） |
| 协程切片：无限循环可按预算中止 | 通过（21.7 ms / 199 片） |
| 主线程约束 | 通过（mainThread=True） |
| 本地脚本实时热加载（D8=B） | 通过（保存后同秒内执行） |

## 2. 环境

| 项 | 值 |
| --- | --- |
| 游戏 | 1.7.5011 rev24（Waylith） |
| 运行时 | net10.0 / Microsoft.NETCore.App 10.0.15-waylithclr.dev |
| Harmony | 3.0.0.0（工作坊 2009463077；本机 `Mods/Harmony` 为旧 2.4.2，未使用） |
| 构建 | 系统 dotnet 9.0.304 的 Roslyn csc 4.14 + 便携 SDK 10.0.402 的 net10 引用程序集 |
| MoonSharp | 3.0.0-beta.1 的 netstandard2.0 构建（371,712 字节） |

## 3. 交付内容

### 3.1 工程结构

```text
About/About.xml                  Mod 元数据（packageId Feng.RimLua，依赖 Harmony）
LoadFolders.xml                  1.7 版本化目录
1.7/Assemblies/RimLua.dll        主程序集（26,112 B）
1.7/Assemblies/MoonSharp.Interpreter.dll
1.7/Lua/core/init.lua            内置核心库占位
Source/RimLua/                   C# 源码（11 个文件）
  RimLuaMod.cs                   入口、设置加载
  RimLuaGameComponent.cs         主线程宿主（Update 泵）
  RimLuaThread.cs                主线程探针
  Lua/LuaRuntime.cs              沙箱、协程切片、宿主全局
  Lua/LuaScriptLoader.cs         实时脚本加载器（FileSystemWatcher）
  Lua/LuaSmokeTests.cs           冒烟测试与报告
  PublicApi/RimLuaApi.cs         最小公共 API
tools/build.ps1                  csc 直编构建脚本
ThirdParty/MoonSharp/            MoonSharp DLL + LICENSE（BSD-3）
Docs/M0-smoke-report.txt         游戏内冒烟报告原件
Docs/M0-quicktest-stdout.txt     快速测试日志（仅 RimLua 行）
```

### 3.2 运行时行为

- 模组加载后，`RimLuaMod` 打印版本与根目录。
- 游戏内第一次 `GameComponentUpdate`（主线程）：
  - 初始化 MoonSharp 硬沙箱；
  - 执行 `1.7/Lua/core/*.lua`；
  - 创建/扫描 `<Config>/RimLua/scripts`，执行本地脚本；
  - 启动 `FileSystemWatcher`（250 ms 去抖）；
  - 执行 M0 冒烟测试。
- 之后每帧：处理脚本变更队列（主线程执行）与冒烟测试的异步断言。

## 4. 验证结果

### 4.1 独立运行时（net10 harness，不启动游戏）

| 验证 | 结果 |
| --- | --- |
| RimLua.dll 在 .NET 10 下加载（解析 Assembly-CSharp / UnityStubs / Waylith / MoonSharp） | 通过 |
| 硬沙箱创建 | 通过 |
| `rim.add(20,22)` → 42 | 通过 |
| 无限循环切片中止 | 通过（25 ms，417 片） |

### 4.2 游戏内快速测试（quicktest）

| # | 验证 | 结果 | 数据 |
| --- | --- | --- | --- |
| V1 | 模组加载 | PASS | `[RimLua] loaded v0.1.0-m0` |
| V2 | MoonSharp 初始化 | PASS | 3.0.0.0 |
| V3 | 硬沙箱（io/os/require/load/loadfile/dofile/loadstring/debug 均 nil） | PASS | 8/8 |
| V4 | 选择性加回 pcall / setmetatable | PASS | 存在；coroutine 未暴露 |
| V5 | 无限循环切片中止 | PASS | 21.7 ms / 199 片 |
| V6 | 64 KiB 源码解析 | PASS | 4 ms |
| V7 | C# 回调 | PASS | 42 |
| V8 | 主线程 | PASS | mainThread=True |
| V9 | 内置 core 脚本执行 | PASS | init.lua 8.2 ms |
| V10 | 本地脚本执行 | PASS | 00_hello.lua 1.0 ms |
| V11 | 实时热加载（写入后自动执行） | PASS | 2.1 ms，保存后同秒执行 |

完整报告见 [M0-smoke-report.txt](M0-smoke-report.txt)，日志见 [M0-quicktest-stdout.txt](M0-quicktest-stdout.txt)。

## 5. 关键发现（影响 M1 设计）

1. **`FinalizeInit` 不保证主线程**：快速测试中 `Game` 构造与 `FinalizeInit` 运行在
   `LongEventHandler.RunEventFromAnotherThread` 的后台线程。第一版在这里初始化，
   冒烟报告 `mainThread=False`。已改为第一次 `GameComponentUpdate` 初始化（主线程）。
2. **`GameComponentTick` 在暂停/快速测试下可能不推进**：改为 `GameComponentUpdate` 做初始化与
   实时脚本泵。副作用：M2 的“每 tick 定时器”需要显式处理暂停语义。
3. **1.7 的 `GameComponent` 需要 `(Game game)` 构造**：基类构造无参，但
   `Verse.Game.FillComponents()` 通过 `Activator.CreateInstance(type, new object[]{ this })` 创建；
   无 `(Game)` 构造会报 `MissingMethodException`。
4. **本机会话中 MSBuild 不可用**：dotnet/MSBuild 子进程创建临时目录被拒绝
   （`MSBuildTemp*.xxx` access denied），因此构建脚本用 csc 直编；`RimLua.csproj` 保留给正常环境。
   另：便携 SDK 的 csc 子进程写输出目录也被拒绝，最终用“系统 dotnet + 系统 csc + 便携 net10 引用程序集”。
5. **quicktest 不会自行退出**（本机 300 s 仍未退出）：测试流程为“启动 → 等待日志 → 强杀 → 读报告”。
   冒烟测试在启动后约 10–15 秒内完成，无需等退出。
6. **MoonSharp 硬沙箱比预期更严**：`pcall`/`setmetatable`/`coroutine` 默认都为 nil；
   按需加回 `ErrorHandling`/`Metatables`，`coroutine` 保持关闭（切片由宿主协程完成）。
7. **实时热加载可做到“保存即执行”**：watcher 事件 + 250 ms 去抖 + Update 泵，实测 2.1 ms 内执行。
   这也意味着该目录的写入权限等价于脚本执行权限，安全边界要在 M1 明确（见报告 §6）。

## 6. 已知限制（M0 不做，M1 处理）

| 限制 | 说明 |
| --- | --- |
| 无持久脚本环境 | 每次执行新建 `Script`，`state` 不跨调用保留 |
| 无输出/内存硬上限 | 只做墙钟 + 切片；源码大小、输出大小、内存配额未强制 |
| 无脚本级权限 | 本地脚本与内置脚本同等能力；能力档未实现 |
| watcher 覆盖有限 | 仅顶层 `*.lua`；删除/重命名只记录，不完全重载 |
| 无控制台 | 只有日志与冒烟报告，不能手动 REPL |
| 无游戏 API | 只有 `rim.version/api_version/ticks/add/is_main_thread` 与 `log` |
| 无存档 | 不写 `ExposeData`，脚本状态不持久 |
| 长稳未测 | 未做 1 小时内存/帧时间测试 |

## 7. M1 建议输入

1. **脚本注册表与持久环境**：每脚本一个长期 `Script` + 独立环境表；`state` 挂在宿主字典。
2. **控制台 REPL**：开发者菜单入口；`reload`/`reload-all`/`list`/`run`；实时编辑窗口。
3. **预算硬化**：源码 ≤64 KiB、输出/状态 ≤64 KiB、命令数 ≤32、脚本数上限、删除处理。
4. **能力档**：本地可信脚本 vs 动态/不可信脚本（API 白名单、只读档）。
5. **事件总线**：Harmony 6 个后置钩子（pawn_death / incident_fired / letter_sent / map_loaded 等）。
6. **再进入 M2**：只读视图与句柄注册表。

## 8. 复现步骤

```powershell
# 1) 构建
powershell -ExecutionPolicy Bypass -File tools/build.ps1

# 2) 快速测试（独立存档目录，避免污染玩家配置）
#    <game>\RimWorld.exe -quicktest -savedatafolder=<repo>\.quicktest\savedata
#    启动后等待约 15 秒，读取：
#    <repo>\.quicktest\savedata\Config\RimLua\smoke-report.txt

# 3) 实时脚本（正常运行游戏时）
#    编辑 <Config>\RimLua\scripts\*.lua，保存后立即执行
```

## 9. 结论

M0 的全部验收目标已达成，MoonSharp 在 RimWorld 1.7 的**加载、沙箱、切片、主线程、实时热加载**均已实测通过；
性能余量充足（解析 4 ms / 64 KiB，切片中止 21.7 ms）。可以进入 M1（沙箱内核与控制台）。
