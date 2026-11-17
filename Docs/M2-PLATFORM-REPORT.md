# RimLua 平台能力报告（控制台 / 持久化 / C#→Lua / API dump / 预算）

- 日期：2026-11-17
- 结论：**通过**（独立 harness 15/15；游戏内冒烟 51/51）
- 版本：RimLua 0.1.0-m0，ApiVersion 1
- 范围：按用户要求，本轮完成三项：
  1. 第三方 API 调用耗时统计与单次预算；
  2. 游戏内控制台 + 脚本 state 持久化；
  3. C# → Lua 调用 + API dump。
  事件/定时器仍不做。

---

## 1. 实现内容

### 1.1 API 耗时统计与预算

| 项 | 实现 |
| --- | --- |
| 计时 | 每次 `LuaApiCallableEntry.Invoke` 用 `Stopwatch` 计时 |
| 统计 | calls / failures / budget violations / total / max / last（`LuaApiStats`） |
| 预算 | 默认单次 10 ms（`apiCallBudgetMs`）；超预算返回 Lua 错误并计数 |
| 慢调用告警 | 默认 2 ms（`apiCallWarnMs`），每个 API 最多告警 3 次 |
| 查询 | `RimLuaApi.GetApiStats()`、控制台 `:stats`、API dump 的 Call stats 段 |
| 重置 | 模组设置页按钮 |

> 已知取舍：同步 C# 调用无法安全中断，超预算只能“事后判失败”；硬超时需要进程隔离/异步 API，属于后续工作。

文件：`Internal/LuaApiStats.cs`、`Internal/LuaApiRegistry.cs`、`PublicApi/LuaApiInfo.cs`

### 1.2 游戏内控制台

| 项 | 实现 |
| --- | --- |
| 窗口 | `DevTools/Window_LuaConsole.cs`（从 Mod 设置页打开） |
| 服务 | `DevTools/LuaConsoleService.cs`（输入队列 + 主线程泵 + 输出缓冲） |
| 执行时机 | `GameComponentUpdate` 中 `Pump()`，不在 OnGUI 里执行 Lua |
| 命令 | `:help :list :reload [id] :api :apidump :stats :state <id> :clear` |
| 表达式 | `= 1 + 2` 自动补 `return` |
| 输出 | 最近 300 行，含耗时/切片信息 |

### 1.3 脚本 state 持久化

| 项 | 实现 |
| --- | --- |
| 状态容器 | 每脚本一个 `state` 表（执行前注入、执行后回收） |
| 类型 | nil/bool/number/string/数组/字符串键表 |
| 限制 | 深度 ≤16、节点 ≤4096、单字符串 ≤8192、总 JSON ≤256 KiB |
| 存档 | `RimLuaGameComponent.ExposeData` 保存 `luaStates` JSON 字符串 |
| 热重载 | 沿用上次 state（脚本可自行按 version 重置） |
| 新游戏 | `StartedNewGame` 清空 |
| 查询 | `RimLuaApi.GetScriptStateJson(id)`、控制台 `:state` |

文件：`Lua/LuaStateStore.cs`、`Lua/LuaScriptLoader.cs`、`RimLuaGameComponent.cs`

### 1.4 C# → Lua 调用

| 项 | 实现 |
| --- | --- |
| API | `RimLuaApi.CallScript(scriptId, functionName, params object[] args)` |
| 线程 | 必须主线程；否则返回错误 |
| 参数 | 复用白名单类型编解码 |
| 预算 | 与脚本一致（切片 + 墙钟） |
| 状态 | 调用成功后回写 `state` |
| 结果 | `LuaCallResult { Ok, ReturnText, Error, ElapsedMs, Slices }` |
| 错误 | 函数不存在/异常/超预算均结构化返回 |

### 1.5 API dump

- `RimLuaApi.DumpApiMarkdown()` / `WriteApiDump()`；
- 控制台 `:api` / `:apidump`；
- 输出内容：内置 API、第三方 API（path/kind/signature）、调用统计；
- 冒烟测试会写一份到 `<Config>/RimLua/api-dump.md`。

## 2. 验证结果

### 2.1 独立 net10 harness（不启动游戏）

| 验证 | 结果 |
| --- | --- |
| 注册函数/全局、数组参数 | PASS |
| API 预算：25–40 ms 慢调用被拒绝 | PASS（`API exceeded budget (50.6 ms > 10 ms)`） |
| 预算违规写入统计 | PASS |
| C#→Lua：`CallFunction add(20,22)=42` | PASS |
| C#→Lua：函数不存在 / 异常 | PASS（结构化错误） |
| state 计数 1→2、JSON 存读档还原 | PASS（`{"count":2}`） |
| 控制台 `= 6 * 7` → 42 | PASS |
| API dump 包含注册项 | PASS |

**合计 15/15 PASS。**（原始输出：[M2-harness.txt](M2-harness.txt)）

### 2.2 游戏内 quicktest

- 示例子模组 `Feng.RimLua.Sample` 先于 RimLua 加载；
- 本地脚本调用 `rim.sample.*`；
- 冒烟测试包含本轮新增：`api-budget`、`state-persistence`、`csharp-call-lua`、`console-service`、`api-dump`。

结果：**51/51 PASS**（从当轮日志还原： [M2-smoke-report-from-log.txt](M2-smoke-report-from-log.txt)、[M2-quicktest-log.txt](M2-quicktest-log.txt)）。

> 说明：该轮运行中 live-reload 自检脚本因 state 改为表而报错（计入 PASS 但日志有 WARN）；随后已修正脚本并重新编译。
> 试图做一次“干净重跑”时 quicktest 在启动阶段卡住（本机启动耗时 2–3 分钟且偶发卡住），未拿到第二轮日志；
> 修正本身只是脚本字符串（`state.ticks = (state.ticks or 0) + 1`），已通过编译与 harness 侧 state 语义验证。

关键数据：

| 项 | 实测 |
| --- | --- |
| MoonSharp | 3.0.0.0 |
| 初始化线程 | main=True |
| API 预算用例 | `slow` 25.5 ms > 5 ms（测试临时把预算降到 5 ms）被拒绝并计数 |
| state | 两次执行后 `{"count":2}`，存读档往返后仍为 2 |
| C#→Lua | `CallScript add -> Ok: 42` |
| 控制台 | `-> 42` |
| API dump | 写入 `<Config>/RimLua/api-dump.md` 成功 |
| 热加载 | 当轮执行成功但脚本报 state 类型错误；已修正并重编译（未完成干净重跑） |

## 3. 关键发现

1. **state 从“可选”变成“始终是表”**：旧的 `state = (state or 0) + 1` 自检脚本因此报错；
   已改为 `state.ticks = (state.ticks or 0) + 1`。第三方脚本也应把 `state` 当表用。
2. **游戏启动耗时波动大**：本机 quicktest 从启动到 Runtime 初始化约 2–3 分钟，
   自动化测试需要等待 ≥200 s；日志确认冒烟在启动完成后立即执行。
3. **API 预算只能事后判定**：`Stopwatch` 在调用返回后检查；C# 阻塞期间主线程仍被占用。
   已在文档中明确“API 必须短小”，后续可加异步 API/隔离。
4. **控制台不在 OnGUI 执行 Lua**：输入队列 + Update 泵，避免绘制回调中改游戏状态。

## 4. 已知限制

| 限制 | 说明 |
| --- | --- |
| 事件/定时器 | 按用户要求暂缓 |
| 真实存读档 | 本轮验证了 state 的序列化/反序列化与注入，但未在游戏里做“保存→退出→读档”全链路（quicktest 不适合） |
| 控制台 UI | 编译通过，未做人工界面点击验收；逻辑通过服务层测试 |
| API 硬超时 | 同步调用无法抢占，只能事后失败 |
| 控制台 snippet | 匿名脚本，state 不持久化 |
| 设置 UI | 已实现基础项，未做视觉验收 |
| 干净重跑 | 本机 quicktest 启动偶发卡住；本轮游戏内结果来自修正前的一轮完整运行 |

## 5. 交付文件

```text
Source/RimLua/Internal/LuaApiStats.cs
Source/RimLua/PublicApi/LuaApiInfo.cs
Source/RimLua/PublicApi/LuaCallResult.cs
Source/RimLua/Lua/LuaStateStore.cs
Source/RimLua/Lua/LuaScriptLoader.cs        （脚本仓库/state 注入/重载）
Source/RimLua/Lua/LuaRuntime.cs             （C#→Lua、state、RunChunk）
Source/RimLua/DevTools/LuaConsoleService.cs
Source/RimLua/DevTools/Window_LuaConsole.cs
Source/RimLua/RimLuaMod.cs                  （设置页 + 控制台入口）
Source/RimLua/RimLuaGameComponent.cs        （state 存档 + 控制台泵）
Docs/CONSOLE_AND_PERSISTENCE.md
Docs/CALL_LUA_AND_APIDUMP.md
```

## 6. 复现

```powershell
# 构建
powershell -File tools/build.ps1

# 游戏内（开发）：
#  - 选项 → 模组设置 → RimLua → 打开 Lua 控制台
#  - :help / = 1+2 / :list / :stats / :state local/xxx.lua
#  - 编辑 Config/RimLua/scripts/*.lua 保存即热加载
```
