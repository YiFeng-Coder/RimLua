# RimLua 第三方 Lua API 扩展指南

- 版本：RimLua 0.1.0-m0（ApiVersion = 1）
- 适用：RimWorld 1.7
- 新手请先读：[TUTORIAL_FOR_SUBMOD_AUTHORS.md](TUTORIAL_FOR_SUBMOD_AUTHORS.md)
- 目标：**任意子模组都能通过 RimLua 注册自己的 C# API，供 Lua 脚本调用。**

> 当前范围说明（2026-11-16）：按用户要求，暂不实现事件/定时器/持久化/控制台，
> 本阶段只做“第三方 C# API 注册”这一件事。

---

## 1. 30 秒上手

### 1.1 C# 侧（子模组）

```csharp
using RimLua;
using RimLua.Api;
using Verse;

[LuaModule("mymod")]
public static class MyModLuaApi
{
    [LuaGlobal("version")]
    public const string Version = "1.0.0";

    [LuaFunction("greet")]
    public static string Greet(string name) => "Hello, " + name + "!";

    [LuaFunction("add")]
    public static int Add(int a, int b) => a + b;

    [LuaFunction("sum")]
    public static int Sum(int[] values)
    {
        int total = 0;
        foreach (int v in values) total += v;
        return total;
    }
}

public class MyMod : Mod
{
    public MyMod(ModContentPack content) : base(content)
    {
        RimLuaApi.RegisterModule(typeof(MyModLuaApi));
    }
}
```

### 1.2 Lua 侧（脚本）

```lua
log.info(rim.mymod.greet("RimWorld"))  -- Hello, RimWorld!
log.info(rim.mymod.add(2, 3))          -- 5
log.info(rim.mymod.sum({1,2,3,4}))     -- 10
log.info(rim.mymod.version)            -- 1.0.0
```

子模组只需要：
1. 引用 `RimLua.dll`（`<Private>false</Private>`，不要打包）；
2. 写一个带 `[LuaModule]` 的类；
3. 在 `Mod` 构造或 `[StaticConstructorOnStartup]` 里调用 `RegisterModule`。

---

## 2. 两种注册方式

### 2.1 特性扫描（推荐）

| 成员 | 含义 |
| --- | --- |
| `[LuaModule("mymod")]` | 类级；`mymod` 是注册到 `rim` 下的命名空间 |
| `[LuaFunction("greet")]` | 方法级；省略名字时使用方法名 |
| `[LuaGlobal("version")]` | 字段/属性级；暴露为常量/值 |

入口：

```csharp
RimLuaApi.RegisterModule(typeof(MyModLuaApi));   // 静态成员
RimLuaApi.RegisterModule(myApiInstance);          // 实例成员（实例方法可用）
RimLuaApi.RegisterAllModules(assembly);           // 扫描整个程序集
```

要求：

- 方法必须是 `public`；`RegisterModule(Type)` 只注册 `static` 成员；
- 字段/属性暴露的必须是 `static`（实例对象请用 `RegisterModule(instance)`）；
- 不支持泛型方法、`ref/out`、指针、超过 16 个参数。

### 2.2 手动注册

```csharp
RimLuaApi.RegisterFunction("mymod.add", (Func<int, int, int>)((a, b) => a + b));
RimLuaApi.RegisterGlobal("mymod.version", "1.0.0");
RimLuaApi.RegisterGlobal("mymod.costs", new[] { 10, 20, 30 });
```

适合少量、动态或无法加特性的 API。

---

## 3. 路径与冲突规则

- 路径相对于 `rim` 根表：`"mymod.greet"` → Lua 中的 `rim.mymod.greet`。
- 每段最多 32 字符，最多 8 段，只允许字母/数字/下划线，且以字母或下划线开头。
- 保留根名（不可注册）：`version`、`api_version`、`api_count`、`add`、`ticks`、`is_main_thread`、`log`。
- 冲突策略：**先注册者优先**；重复路径、或“函数/值与其子路径冲突”时返回 `false` 并在日志中报错。
- 注册成功返回 `true`；失败返回 `false`，不会影响其他 API。

---

## 4. 支持的类型

### 4.1 参数（Lua → C#）

| C# 参数类型 | Lua 传入 |
| --- | --- |
| `string` / `bool` | 对应类型 |
| `int/long/short/byte/uint/ulong/ushort/sbyte` | 数字（必须是整数，越界报错） |
| `float/double/decimal` | 数字 |
| 枚举 | 字符串名字（忽略大小写）或数字 |
| `IntVec3` | `{ x = ..., y = ..., z = ... }` |
| `T[]`、`List<T>` | Lua 数组（从 1 开始） |
| `DynValue` / `Table` | 原样传递（给高级用法） |
| `LuaApiContext` | 宿主自动注入（不占 Lua 参数位置） |
| `object` | 基础类型/表（UserData 会被拒绝） |

### 4.2 返回值（C# → Lua）

支持：`void`、`string`、`bool`、数值类型、枚举（转字符串）、`IntVec3`、
`IDictionary`（转 Lua 表）、`IEnumerable`（转 Lua 数组）、`DynValue`/`Table`。

**不支持**返回任意 CLR 对象（禁止把 `Pawn`、`Map`、自定义类实例等直接丢给 Lua）。
这样既避免泄漏内部对象，也保证 Lua 侧只能看到纯数据。

---

## 5. LuaApiContext

如果 API 需要知道调用上下文，把 `LuaApiContext` 放在任意参数位置：

```csharp
[LuaFunction("info")]
public static string Info(LuaApiContext ctx)
    => "api=" + ctx.ApiName + ", tick=" + ctx.GameTick + ", main=" + ctx.IsMainThread;
```

| 属性 | 含义 |
| --- | --- |
| `ApiName` | 完整路径，如 `mymod.info` |
| `GameTick` | 当前游戏 tick（无游戏时为 0） |
| `IsMainThread` | 是否主线程（RimLua 保证正常调用为 true） |

### 5.1 API dump 与 C#→Lua

- 已注册 API 可导出为 Markdown：`RimLuaApi.DumpApiMarkdown()` / `WriteApiDump()`；
  控制台命令 `:api` / `:apidump`。
- C# 可主动调用脚本函数：`RimLuaApi.CallScript(scriptId, functionName, args)`。
- 详见 [CALL_LUA_AND_APIDUMP.md](CALL_LUA_AND_APIDUMP.md)。

---

## 6. 生命周期与加载顺序

- RimLua 的注册表是**静态、线程安全**的；子模组可以在 RimLua 初始化之前或之后注册。
- **不要求** RimLua 先加载：子模组先 `RegisterModule`、RimLua 后加载也能生效。
- 运行时初始化完成后，注册表会在每次创建 Lua 环境时重建，因此**新注册的 API 对下一次脚本执行立即可见**。
- `RimLuaApi.WhenReady(Action)` 可在运行时就绪后回调（若已就绪则立即执行）。
- 状态查询：`RimLuaApi.IsReady`、`RimLuaApi.RegisteredApiCount`、`RimLuaApi.ApiVersion`。

---

## 7. 错误处理

- 参数类型/数量错误、返回值不受支持、C# 抛异常，都会变成 Lua 运行时错误（`ScriptRuntimeException`）。
- 脚本可以用 `pcall` 捕获，例如：

```lua
local ok, err = pcall(function() rim.sample.fail() end)
log.info("caught=" .. tostring(ok) .. " err=" .. tostring(err))
```

- 宿主会记录前 3 次失败到 `Player.log`（`[RimLua][api]`），避免日志刷屏。
- 单个 API 失败只影响本次调用，不会影响 RimLua 或其他 API。

---

## 8. 性能与安全

- 每次 Lua 调用都会计时并计入统计；默认**单次预算 10 ms**、慢调用告警 2 ms。
  超预算会返回 Lua 错误并记录（C# 调用无法中途抢占，宿主只能事后判定失败）。
  详见 [CALL_LUA_AND_APIDUMP.md](CALL_LUA_AND_APIDUMP.md)。
- 请保持 API 短小（目标 < 1 ms），长耗时逻辑请异步/分帧处理。
- C# API 代码是**可信代码**：它能做 RimWorld 允许的一切，请自行校验业务参数。
- Lua 侧仍受 RimLua 的硬沙箱与切片预算限制，不会因为 API 注册而获得 IO/反射能力。
- 不要把敏感对象、文件句柄、委托等返回给 Lua。

---

## 9. 示例工程

完整可编译示例：[Examples/SampleExtension/](../Examples/SampleExtension/)

- 注册 `rim.sample.greet/add/sum/info/version/fail`
- 演示字符串、整数、数组、全局常量、上下文、异常
- 构建：`powershell -File Examples/SampleExtension/build-sample.ps1`
- 将示例目录复制到 `Mods/` 启用即可

---

## 10. 当前限制（按用户要求暂缓）

| 未实现 | 说明 |
| --- | --- |
| 事件/定时器 | 不做 `rim.event` / `rim.timer`；脚本只在启动/热加载/C# 调用时执行 |
| 游戏对象 API | 没有 `rim.game/map/pawn` 等；子模组可自己通过 API 暴露安全数据 |
| 注销/覆盖 API | 先注册者优先，不提供运行时注销 |
| C# ↔ Lua 复杂值 | 参数/返回值只支持白名单类型，不支持函数/委托互传 |
| 硬超时 | API 调用无法中途抢占，只能事后判失败；异步/隔离在后续版本 |

---

## 11. 测试证据

- 独立 net10 harness：注册/冲突/保留名/调用/数组/全局/错误/预算/统计/API dump 全部通过。
- 游戏内（quicktest）：示例子模组**先于** RimLua 加载（`rimluaReady=False`），注册 6 个 API；
  RimLua 初始化日志 `third-party APIs=6`；Lua 脚本成功调用 `rim.sample.*`，`pcall` 捕获异常。
- 同轮冒烟 **51/51 PASS**（含 `thirdparty-api`、`api-budget`、`state-persistence`、`csharp-call-lua`、`console-service`、`api-dump`）。
- 详细结果见 [M2-PLATFORM-REPORT.md](M2-PLATFORM-REPORT.md)；历史阶段见 [M1-EXTENSION-API-REPORT.md](M1-EXTENSION-API-REPORT.md)。
