# RimLua 子模组开发教程

> 面向：想给自己 RimWorld 模组增加 Lua 脚本能力的 C# 开发者
> 适用版本：RimWorld 1.7 / RimLua 0.1.0-m0（ApiVersion 1）
> 阅读前提：会写基础 RimWorld C# 模组，了解 Lua 基本语法

---

## 0. 十分钟目标

完成后你的子模组可以：

1. 向 Lua 暴露自己的 C# 函数和常量；
2. 在 Lua 脚本里调用 `rim.yourmod.xxx(...)`；
3. 从 C# 主动调用自己脚本里的函数；
4. 使用 RimLua 的控制台调试、查看 API dump、查看调用统计。

最终形态：

```lua
log.info(rim.mymod.greet("RimWorld"))    -- Hello, RimWorld!
log.info(rim.mymod.add(2, 3))            -- 5
local info = rim.mymod.colony_info()     -- 返回一个表
log.info(info.colonists)
```

---

## 1. 前置条件

| 项 | 要求 |
| --- | --- |
| 游戏 | RimWorld 1.7（当前为 1.7.5011 rev24 / Waylith / .NET 10） |
| 依赖模组 | Harmony 3.x（RimLua 已声明依赖）、RimLua |
| 开发环境 | .NET SDK 10.x（或能编译 net10.0 的 VS/Rider） |
| 引用程序集 | `RimLua.dll`、`Assembly-CSharp.dll`、`UnityStubs.dll`、`Waylith.dll` |

**不要**把 `RimLua.dll` 或 `MoonSharp.Interpreter.dll` 打进你自己的 `Assemblies/`，否则会出现重复程序集/版本冲突。

推荐在你的 `About.xml` 声明依赖：

```xml
<modDependencies>
  <li>
    <packageId>Feng.RimLua</packageId>
    <displayName>RimLua</displayName>
    <downloadUrl>https://steamcommunity.com/workshop/</downloadUrl>
  </li>
</modDependencies>
```

> RimLua 的注册表本身不要求加载顺序（先注册后加载也能生效），但声明依赖能让游戏保证 RimLua 存在，并给出明确的缺依赖提示。

---

## 2. 五分钟接入

### 2.1 C# 工程

一个 `net10.0` 类库，输出到你的 mod 的 `Assemblies/`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AssemblyName>MyMod</AssemblyName>
    <RootNamespace>MyMod</RootNamespace>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <OutputPath>..\..\Assemblies\</OutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Assembly-CSharp"><HintPath>$(GameDir)/Assembly-CSharp.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityStubs"><HintPath>$(GameDir)/UnityStubs.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="Waylith"><HintPath>$(GameDir)/Waylith.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="RimLua">
      <HintPath>..\..\..\Mods\AIStory\1.7\Assemblies\RimLua.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> `HintPath` 请按你的实际目录调整；关键是 `Private=false`（运行时由游戏加载 RimLua）。

### 2.2 写 API 模块

```csharp
using System.Collections.Generic;
using RimLua;
using RimLua.Api;
using Verse;

namespace MyMod;

[LuaModule("mymod")]
public static class MyModLuaApi
{
    [LuaGlobal("version")]
    public const string Version = "1.0.0";

    [LuaFunction("greet")]
    public static string Greet(string name) => "Hello, " + name + "!";

    [LuaFunction("add")]
    public static int Add(int a, int b) => a + b;

    [LuaFunction("colony_info")]
    public static Dictionary<string, object> ColonyInfo()
    {
        int colonists = 0;
        foreach (Map map in Find.Maps)
            colonists += map.mapPawns.FreeColonists.Count;
        return new Dictionary<string, object>
        {
            { "colonists", colonists },
            { "ticks", Find.TickManager.TicksGame }
        };
    }
}
```

### 2.3 在 Mod 构造里注册

```csharp
using RimLua;
using Verse;

namespace MyMod;

public class MyMod : Mod
{
    public MyMod(ModContentPack content) : base(content)
    {
        int count = RimLuaApi.RegisterModule(typeof(MyModLuaApi));
        Log.Message("[MyMod] registered " + count + " Lua API entries");
    }
}
```

### 2.4 构建、进游戏验证

1. 编译并把 DLL 放进你的 mod 的 `Assemblies/`；
2. 启用 Harmony、RimLua、你的 mod；
3. 查看 `Player.log` 是否出现：

```text
[RimLua][api] registered module MyMod.MyModLuaApi (mymod): 4 entries
[MyMod] registered 4 Lua API entries
```

4. 打开 **选项 → 模组设置 → RimLua → 打开 Lua 控制台**，输入：

```
= rim.mymod.greet("RimWorld")
= rim.mymod.add(2, 3)
:api
```

看到结果即接入成功。

---

## 3. 三种注册方式

### 3.1 特性扫描（推荐）

```csharp
[LuaModule("mymod")]          // 命名空间，对应 rim.mymod.*
public static class MyApi
{
    [LuaFunction("greet")] public static string Greet(string n) => "hi " + n;
    [LuaGlobal("version")] public const string Version = "1.0";
}

RimLuaApi.RegisterModule(typeof(MyApi));
```

- 方法必须 `public`；`RegisterModule(Type)` 只注册 `static` 成员；
- 字段/属性必须 `static` 才能通过 `Type` 注册；
- 不支持泛型方法、`ref/out`、指针、超过 16 个参数。

### 3.2 实例模块

需要访问 mod 实例状态时，用对象注册：

```csharp
[LuaModule("mymod")]
public sealed class MyApi
{
    private readonly int offset;
    public MyApi(int offset) { this.offset = offset; }

    [LuaFunction("offset_value")]
    public int OffsetValue() => offset;
}

RimLuaApi.RegisterModule(new MyApi(42));
```

实例方法会被注册；静态成员也可以放在同一个类里。

### 3.3 手动注册

```csharp
RimLuaApi.RegisterFunction("mymod.add", (Func<int, int, int>)((a, b) => a + b));
RimLuaApi.RegisterGlobal("mymod.name", "MyMod");
RimLuaApi.RegisterGlobal("mymod.costs", new[] { 10, 20, 30 });
```

### 3.4 整个程序集扫描

```csharp
RimLuaApi.RegisterAllModules(typeof(MyMod).Assembly);
```

扫描程序集中所有带 `[LuaModule]` 的类（静态成员）。

### 3.5 注册时机

- 在 `Mod` 构造、`[StaticConstructorOnStartup]`、你自己的 `GameComponent` 里都可以；
- 注册表线程安全；RimLua 初始化前后注册都会生效；
- 注册后下一次创建 Lua 环境时生效（即下一次脚本执行）；
- `RimLuaApi.WhenReady(Action)` 可在 RimLua 运行时就绪后回调（若已就绪则立即执行）。

---

## 4. 路径、命名与冲突规则

- 注册路径相对于 `rim`：`"mymod.greet"` → Lua 里的 `rim.mymod.greet`；
- 每段只允许字母/数字/下划线，以字母或下划线开头，最多 32 字符；最多 8 段；
- 保留根名（不可占用）：`version`、`api_version`、`api_count`、`add`、`ticks`、`is_main_thread`、`log`、`script_id`；
- 冲突策略：**先注册者优先**；重复路径或父/子路径冲突会返回 `false` 并写日志；
- 建议：模块名用你的 mod 的短名（小写），函数用动词/名词，避免 `mymod.add2` 这类临时名字。

---

## 5. 参数与返回值（类型编解码）

### 5.1 Lua → C#（参数）

| C# 参数 | Lua 传入 |
| --- | --- |
| `string` / `bool` | 对应类型 |
| `int/long/short/byte/uint/ulong/ushort/sbyte` | 整数；小数/越界会报错 |
| `float/double/decimal` | 数字 |
| 枚举 | 字符串名字（忽略大小写）或数字 |
| `IntVec3` | `{ x = 1, y = 0, z = 2 }` |
| `T[]` / `List<T>` | Lua 数组（从 1 开始） |
| `DynValue` / `Table` | 原样传入（高级用法） |
| `LuaApiContext` | 宿主自动注入，不占 Lua 参数位 |
| `object` | 基础类型/表；UserData 会被拒绝 |

### 5.2 C# → Lua（返回值）

支持：`void`、`string`、`bool`、数值、枚举（转字符串）、`IntVec3`、`IDictionary`（转表）、`IEnumerable`（转数组）、`DynValue`/`Table`。

**不支持**返回任意 CLR 对象（`Pawn`、`Map`、你自己的类实例等）。如果需要返回游戏数据，请转成基础类型或字典/数组，例如：

```csharp
[LuaFunction("pawn_names")]
public static List<string> PawnNames()
{
    var names = new List<string>();
    foreach (Pawn pawn in Find.CurrentMap.mapPawns.AllPawns) names.Add(pawn.Name.ToStringShort);
    return names;
}
```

### 5.3 LuaApiContext

```csharp
[LuaFunction("info")]
public static string Info(LuaApiContext ctx)
    => "api=" + ctx.ApiName + ", tick=" + ctx.GameTick + ", main=" + ctx.IsMainThread;
```

| 属性 | 含义 |
| --- | --- |
| `ApiName` | 完整路径 |
| `GameTick` | 当前 tick（无游戏时为 0） |
| `IsMainThread` | 正常调用时为 true |

### 5.4 错误处理

- 参数类型/数量不对、返回值类型不支持、C# 抛异常，都会变成 Lua 运行时错误；
- 脚本侧可以用 `pcall` 捕获：

```lua
local ok, err = pcall(function() rim.mymod.risky() end)
if not ok then log.warn("failed: " .. tostring(err)) end
```

- 宿主会记录每个 API 前 3 次失败，避免刷屏；单个 API 失败不影响其他 API。

---

## 6. 性能、预算与统计

- 默认单次 API 调用预算 **10 ms**，慢调用告警 **2 ms**；
- 超预算会返回 Lua 错误并计入统计（同步 C# 无法中途抢占，只能事后判定）；
- 原则：API 必须短小、非阻塞；不要在 API 里 `Thread.Sleep`、等网络、遍历全地图大量对象；
- 需要耗时逻辑时：快速返回任务 ID/队列，把实际工作放到 tick/协程里；
- 查看统计：控制台 `:stats`，或 C# `RimLuaApi.GetApiStats()`；
- 设置页可配置预算/告警阈值，或关闭“耗时预算/告警”（关闭后只统计不强制）。

---
## 6. 脚本侧：怎么写、怎么调试

### 6.1 脚本放在哪里

| 位置 | 说明 | 是否热加载 |
| --- | --- | --- |
| RimLua 自带 `1.7/Lua/core/*.lua` | RimLua 核心库 | 否 |
| `<Config>/RimLua/scripts/*.lua` | 玩家本地脚本 | **是**（保存后 250 ms 去抖，下一帧执行） |
| 你的 mod 自带脚本 | 通过 C# `LuaScriptLoader.LoadFileNow` 加载（见 §7） | 否（重启/手动重载） |

> 当前版本 RimLua 不会自动扫描其他模组的 `Lua/` 目录；子模组需要用 §7 的方式主动加载自己的脚本。

### 6.2 脚本里能用的内置能力

| 名称 | 说明 |
| --- | --- |
| `rim.version` / `rim.api_version` | RimLua 版本信息 |
| `rim.script_id` | 当前脚本 id |
| `rim.ticks()` | 当前游戏 tick |
| `rim.is_main_thread()` | 是否主线程 |
| `rim.api_count` | 已注册 API 数量 |
| `log.info/warn/error(...)` | 写 Player.log |
| `state` | 当前脚本的持久状态表（始终是表） |
| 标准库 | `string/table/math/bit32/pcall/setmetatable` 等（无 io/os/require） |

调用你的 API：

```lua
local info = rim.mymod.colony_info()
log.info("colonists=" .. info.colonists .. ", tick=" .. info.ticks)
```

### 6.3 持久化状态

```lua
state.runs = (state.runs or 0) + 1
log.info("script ran " .. state.runs .. " times")
```

- `state` 自动随存档保存/恢复；热重载保留；开新游戏清空；
- 只支持纯数据：nil/bool/number/string/数组/字符串键表；
- 函数/userdata/协程/环引用会被丢弃；
- 限制：深度 16、节点 4096、单字符串 8192、总 JSON 默认 256 KiB；
- 执行失败时不回写 state。

### 6.4 调试

1. 控制台：`= rim.mymod.greet("x")`、`:list`、`:reload`、`:stats`、`:state local/xxx.lua`；
2. 改 `Config/RimLua/scripts/*.lua` 保存即热加载；
3. Player.log 搜索 `[RimLua]`、`[RimLua][api]`、`[RimLua][loader]`；
4. `:apidump` 导出当前 API 列表核对路径/签名。

---

## 7. 子模组自带 Lua 脚本

推荐模式：在 `RimLuaApi.WhenReady` 回调里加载自己 mod 的脚本。

```csharp
using System.IO;
using RimLua;
using Verse;

public class MyMod : Mod
{
    public MyMod(ModContentPack content) : base(content)
    {
        RimLuaApi.RegisterModule(typeof(MyModLuaApi));

        RimLuaApi.WhenReady(() =>
        {
            string dir = Path.Combine(Content.RootDir, "Lua");
            if (!Directory.Exists(dir)) return;

            foreach (string file in Directory.GetFiles(dir, "*.lua"))
            {
                string idPrefix = "mod/" + Content.PackageId;
                LuaRunResult result = LuaScriptLoader.LoadFileNow(file, idPrefix, false);
                if (!result.IsSuccess)
                    Log.Error("[MyMod] lua load failed " + file + ": " + result.Error);
            }
        });
    }
}
```

- 脚本 id 形如 `mod/your.packageid/init.lua`，后续 `CallScript`、`:state` 用这个 id；
- `WhenReady` 在主线程执行；
- 这些脚本不参与 `Config/RimLua/scripts` 的 watcher 热加载；开发期可先把脚本放 Config 目录迭代，稳定后再随 mod 分发；
- 如果脚本依赖其他 mod 的 API，注意注册顺序（都通过 `RegisterModule`，通常没问题）。

---

## 8. C# 主动调用 Lua

```csharp
LuaCallResult result = RimLuaApi.CallScript(
    "mod/your.packageid/init.lua",  // 脚本 id
    "on_event",                     // 脚本里的全局函数
    "hello", 42);                   // 参数

if (result.Ok)
    Log.Message("lua returned: " + result.ReturnText);
else
    Log.Warning("lua failed: " + result.Error);
```

| 项 | 规则 |
| --- | --- |
| 线程 | 必须主线程（否则返回错误） |
| 函数 | 脚本里的全局函数 |
| 参数 | 与 API 参数相同的白名单类型 |
| 返回 | `LuaCallResult { Ok, ReturnText, Error, ElapsedMs, Slices }` |
| 状态 | 调用成功后自动回写 `state` |

脚本侧：

```lua
function on_event(name, value)
  state.calls = (state.calls or 0) + 1
  return name .. ":" .. value .. " (call " .. state.calls .. ")"
end
```

---

## 9. API dump 与文档

- 控制台 `:apidump` 或 C# `RimLuaApi.WriteApiDump()`；
- 输出到 `<Config>/RimLua/api-dump.md`，包含：
  - 内置 API；
  - 你的 API（path / kind / signature）；
  - 调用统计（次数/失败/预算违规/耗时）。

建议发布前导出一次，贴到你的 mod 文档里，方便脚本作者查阅。

---

## 10. 完整示例

可直接参考仓库示例：[Examples/SampleExtension](../Examples/SampleExtension/)。

目录：

```text
SampleExtension/
  About/About.xml
  Assemblies/RimLuaSample.dll
  Source/RimLuaSample/
    SampleLuaApi.cs
    RimLuaSampleMod.cs
    RimLuaSample.csproj
```

`About.xml`：

```xml
<ModMetaData>
  <name>RimLua Sample API</name>
  <author>YourName</author>
  <packageId>YourName.RimLuaSample</packageId>
  <supportedVersions><li>1.7</li></supportedVersions>
  <modDependencies>
    <li><packageId>Feng.RimLua</packageId><displayName>RimLua</displayName></li>
  </modDependencies>
</ModMetaData>
```

API 类：

```csharp
[LuaModule("sample")]
public static class SampleLuaApi
{
    [LuaGlobal("version")] public const string Version = "1.0.0";

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

    [LuaFunction("info")]
    public static string Info(LuaApiContext ctx)
        => "api=" + ctx.ApiName + ", tick=" + ctx.GameTick + ", main=" + ctx.IsMainThread;
}
```

Lua 测试脚本（`Config/RimLua/scripts/test.lua`）：

```lua
log.info("sample greet: " .. rim.sample.greet("RimWorld"))
log.info("sample add: " .. rim.sample.add(2, 3))
log.info("sample sum: " .. rim.sample.sum({1, 2, 3, 4}))
log.info("sample version: " .. rim.sample.version)
log.info("sample info: " .. rim.sample.info())
```

预期日志：

```text
[RimLua][script] sample greet: Hello, RimWorld!
[RimLua][script] sample add: 5
[RimLua][script] sample sum: 10
[RimLua][script] sample version: 1.0.0
[RimLua][script] sample info: api=sample.info, tick=..., main=true
```

---

## 11. 常见问题

| 现象 | 原因 / 解决 |
| --- | --- |
| Lua 里 `rim.mymod` 是 nil | API 未注册/路径拼错/`[LuaModule]` 命名空间不对/旧 DLL 未替换；用 `:apidump` 核对 |
| 注册返回 false | 路径冲突或用了保留名；看 Player.log 的 `[RimLua][api]` |
| 报 `unsupported return type` | 返回了 Pawn/Map/自定义类；改成基础类型/字典/数组 |
| 报 `expected number, got String` | Lua 传了字符串给数值参数；检查参数顺序/类型 |
| 报 `API exceeded budget` | API 太慢；优化或改成异步/分帧；可调预算但不要掩盖阻塞 |
| `CallScript` 报 `script not found` | 脚本 id 不对（`core/...`、`local/...`、`mod/<packageId>/...`）或用 `:list` 查看 |
| `CallScript` 报 `function not found` | 函数不是全局函数，或脚本加载失败 |
| `state` 报错 | `state` 永远是表；用 `state.x`，不要 `state = 0` |
| 改了脚本没生效 | 只有 `Config/RimLua/scripts/*.lua` 自动热加载；mod 自带脚本需重启或手动 `:reload` |
| 存档后状态丢了 | 执行失败不会写状态；state 必须纯数据；超过大小限制会清空并报错 |
| 控制台没反应 | 控制台执行在下一帧；确认 RimLua 初始化成功（Player.log） |
| 两个 mod 都注册了同名 API | 先注册者优先；给你的 API 加自己的命名空间 |

---

## 12. 发布检查清单

- [ ] `About.xml`：`packageId`、`supportedVersions` 含 `1.7`、依赖 `Feng.RimLua`；
- [ ] 不打包 `RimLua.dll` / `MoonSharp.Interpreter.dll`；
- [ ] 注册代码在 `Mod` 构造或 `[StaticConstructorOnStartup]` 中执行，并有日志；
- [ ] API 路径有统一前缀（如 `yourmod.*`），无保留名/冲突；
- [ ] 所有 API 返回白名单类型，不泄漏游戏对象；
- [ ] 单个 API 实测 < 2 ms，无阻塞/IO/长时间循环；
- [ ] 提供 Lua 示例与 `:apidump` 输出；
- [ ] 在自己的 mod 文档里写清 RimLua 版本要求（`ApiVersion`）与加载脚本的方法；
- [ ] 测试：缺 RimLua 时的提示、重复注册、错误参数、`pcall`、热重载、存读档。

---

## 13. 当前限制（规划中）

- 没有事件/定时器 API（`rim.event` / `rim.timer` 未实现）；
- 没有内置游戏对象 API（`rim.game/map/pawn`）；需要的话由你的 mod 自己暴露安全数据；
- 不支持 API 注销/覆盖；
- 同步 C# API 无法硬中断；
- 子模组自带 `Lua/` 目录尚未自动扫描（用 §7 的模式手动加载）。

---

## 附录 A：RimLuaApi 速查

| 成员 | 用途 |
| --- | --- |
| `RegisterModule(Type/object)` | 扫描特性并注册 |
| `RegisterAllModules(Assembly)` | 扫描整个程序集 |
| `RegisterFunction(path, Delegate)` | 手动注册函数 |
| `RegisterGlobal(path, object)` | 手动注册常量/值 |
| `WhenReady(Action)` | 运行时就绪回调 |
| `CallScript(id, fn, args...)` | C# 调 Lua |
| `GetLoadedScripts()` | 已加载脚本列表 |
| `GetRegisteredApis()` | API 信息列表 |
| `GetApiStats()` | 调用统计 |
| `GetScriptStateJson(id)` | 查看 state JSON |
| `DumpApiMarkdown()` / `WriteApiDump()` | API dump |
| `IsReady` / `ApiVersion` / `Version` | 状态与版本 |

## 附录 B：控制台命令速查

| 命令 | 作用 |
| --- | --- |
| `:help` | 帮助 |
| `:list` | 已加载脚本 |
| `:reload [id]` | 重载全部/单个 |
| `:api` | 打印 API dump |
| `:apidump` | 写 api-dump.md |
| `:stats` | API 调用统计 |
| `:state <id>` | 查看脚本 state |
| `:clear` | 清空输出 |
| `= <expr>` | 执行表达式 |
