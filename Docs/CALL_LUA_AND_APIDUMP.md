# C# → Lua 调用 与 API Dump

- 版本：RimLua 0.1.0-m0，ApiVersion 1
- 适用：RimWorld 1.7

---

## 1. C# 主动调用 Lua

### 1.1 入口

```csharp
using RimLua;
using RimLua.Api;

LuaCallResult result = RimLuaApi.CallScript(
    "local/myscript.lua",        // 脚本 id
    "on_my_event",               // 脚本里的全局函数名
    "hello", 42);                // 参数（可选）

if (!result.Ok)
{
    Log.Warning("lua call failed: " + result.Error);
}
else
{
    Log.Message("lua returned: " + result.ReturnText + " in " + result.ElapsedMs + " ms");
}
```

### 1.2 规则

| 项 | 说明 |
| --- | --- |
| 脚本 id | `core/<file>.lua` 或 `local/<file>.lua`；`:list` 可查 |
| 调用线程 | **必须主线程**；非主线程返回错误 |
| 函数 | 必须是脚本里定义的全局函数 |
| 参数 | 基础类型/string/bool/枚举/`IntVec3`/数组/`List<T>`/`Dictionary`（见扩展指南） |
| 返回值 | 转 `ReturnText`（字符串形式）；复杂结果建议脚本内转成表/字符串 |
| 预算 | 与普通脚本相同：`sliceInstructionBudget` + `scriptWallClockBudgetMs` |
| 状态 | 调用成功后自动把 `state` 写回宿主 |
| 错误 | 函数不存在/异常/超预算 → `Ok=false` 且 `Error` 有原因 |

### 1.3 脚本侧示例

```lua
-- Config/RimLua/scripts/myscript.lua
function on_my_event(name, value)
  state.calls = (state.calls or 0) + 1
  return name .. ":" .. value .. " (call " .. state.calls .. ")"
end
```

### 1.4 已加载脚本查询

```csharp
foreach (LuaScriptInfo script in RimLuaApi.GetLoadedScripts())
{
    Log.Message(script.Id + " -> " + (script.LastRun != null ? script.LastRun.Status : "<none>"));
}
```

`LuaScriptInfo` 包含 `Id/FilePath/Source/Hash/LoadedAt/LastRun/LastError`。

---

## 2. API Dump

### 2.1 用法

```csharp
string markdown = RimLuaApi.DumpApiMarkdown();  // 直接拿文本
string path = RimLuaApi.WriteApiDump();        // 写入 <Config>/RimLua/api-dump.md
```

控制台命令：`:api`（打印）、`:apidump`（写文件）。

### 2.2 内容

- 内置 API（`rim.version`、`log.*` 等）；
- 已注册的第三方 API（路径 / 类型 / 签名）；
- 调用统计（calls / failures / budget violations / total / max）。

示例：

```markdown
## Registered (6)

| path | kind | signature |
| --- | --- | --- |
| sample.add | function | int sample.add(int, int) |
| sample.greet | function | string sample.greet(string) |
| sample.version | value | String sample.version |
```

### 2.3 生成时机

- 手动：`:apidump` / `RimLuaApi.WriteApiDump()`；
- 冒烟测试会写一份到 `<Config>/RimLua/api-dump.md` 作为自检。

---

## 3. API 调用耗时统计与预算

- 每次第三方 API 调用都会计时（`Stopwatch`），记录：次数、失败、预算违规、总耗时、最大耗时。
- 默认预算：单次 **10 ms**；慢调用告警阈值：**2 ms**（可在模组设置修改）。
- 超预算时：**返回 Lua 错误**并计入统计；注意 C# 调用无法被中途抢占，宿主只能“事后判定失败”。
  因此第三方 API 必须自行保证短小、非阻塞。
- 查询：`RimLuaApi.GetApiStats()`、控制台 `:stats`、API dump 的 Call stats 段。
- 重置：模组设置页按钮 / `LuaApiStats.Reset()`（内部）。

> 设计取舍：同步 C# 无法安全中断，真正的“硬超时”需要进程隔离或异步 API；当前版本先做可观测与失败判定。

---

## 4. 限制

- 不支持把 Lua 函数作为参数/返回值传给 C#；
- 不支持 C# 侧 await/异步调用 Lua；
- 非主线程调用直接失败（不会排队）；
- 脚本被删除/加载失败时，CallScript 返回 `script not found`。
