# RimLua 第三方 Lua API 扩展报告

- 日期：2026-11-16
- 结论：**通过**（子模组先于 RimLua 加载仍可注册；游戏内脚本成功调用；冒烟 32/32）
- 版本：RimLua 0.1.0-m0，ApiVersion = 1
- 范围：按用户要求，本阶段**只做**“任意子模组通过 RimLua 注册 Lua C# API”，不实现事件/定时器/持久化/控制台

---

## 1. 需求

> “任意其他子 mod 都可以通过该模组来添加 Lua 相应的 C# API。”

RimLua 需要提供一个稳定的 C# 扩展入口，使第三方模组：

1. 可以把 C# 方法/常量暴露成 Lua 函数/值；
2. 不依赖 RimLua 的加载顺序（先于或后于 RimLua 加载都能生效）；
3. 支持基本类型编解码、错误处理、版本与冲突规则；
4. 不破坏 Lua 沙箱与主线程约束。

## 2. 实现的 API 面

### 2.1 入口（`RimLua.RimLuaApi`）

| API | 用途 |
| --- | --- |
| `RegisterModule(Type)` | 扫描静态类上的 `[LuaModule]/[LuaFunction]/[LuaGlobal]` |
| `RegisterModule(object)` | 同上，支持实例方法 |
| `RegisterAllModules(Assembly)` | 扫描整个程序集 |
| `RegisterFunction(path, Delegate)` | 手动注册函数 |
| `RegisterGlobal(path, object)` | 手动注册常量/值 |
| `WhenReady(Action)` | 运行时就绪回调（主线程；已就绪则立即执行） |
| `IsReady` / `RegisteredApiCount` / `ApiVersion` | 状态与版本 |
| `ScriptsDirectory` / `SmokeReportPath` | 路径查询 |

### 2.2 特性（`RimLua.Api`）

- `[LuaModule("mymod")]`：命名空间
- `[LuaFunction("greet")]`：函数
- `[LuaGlobal("version")]`：常量/值
- `LuaApiContext`：宿主注入 `ApiName/GameTick/IsMainThread`
- `LuaApiException`：注册/参数/返回值错误

### 2.3 注册表行为

- 静态、线程安全；允许在 RimLua 初始化前后任意时刻注册。
- 每次创建 Lua 环境时重建 API 表，因此“注册后下一次脚本执行即生效”。
- 路径规则：相对 `rim`，每段字母/数字/下划线，最多 8 段；保留根名（version/api_version/api_count/add/ticks/is_main_thread/log）不可占用。
- 冲突：先注册者优先，返回 `false` 并记录日志；函数与子路径互斥。

### 2.4 类型编解码

- 参数：基础类型、字符串、枚举（名字/数字）、`IntVec3`、`T[]`/`List<T>`、`DynValue`/`Table`、`object`、`LuaApiContext`。
- 返回：`void`、基础类型、字符串、枚举、`IntVec3`、`IDictionary`→Lua 表、`IEnumerable`→Lua 数组、`DynValue`/`Table`。
- **禁止**返回任意 CLR 对象（避免把 Pawn/Map/自定义实例泄漏给 Lua）。
- 参数数量/类型错误、C# 异常 → Lua 运行时错误，可被 `pcall` 捕获；宿主仅记录每个 API 前 3 次失败。

## 3. 验证

### 3.1 独立 net10 harness（不启动游戏）

| 验证 | 结果 |
| --- | --- |
| 注册函数/全局 | PASS |
| 重复路径拒绝 | PASS（返回 false） |
| 保留名拒绝 | PASS（`version`） |
| 前缀冲突拒绝 | PASS（`harness.add.deep`） |
| `rim.harness.add(20,22)` | PASS（42） |
| `int[]` 参数求和 | PASS（10） |
| 全局字符串 | PASS（harness） |
| `rim.api_count` | PASS |
| 错误参数被拒 | PASS（expected number, got String） |

### 3.2 游戏内跨模组测试（quicktest）

测试条件：
- 子模组 `Feng.RimLua.Sample` **先于** `Feng.RimLua` 加载（最坏顺序）；
- 子模组 Mod 构造时 `RegisterModule`，日志显示 `rimluaReady=False`；
- 本地脚本 `zz_sample_test.lua` 调用 `rim.sample.*`。

日志证据：

```text
[RimLua][api] registered module RimLuaSample.SampleLuaApi (sample): 6 entries
[RimLuaSample] registered 6 Lua API entries, rimluaReady=False
[RimLua] MoonSharp 3.0.0.0 initialized on main thread=True, third-party APIs=6
[script] SAMPLE greet=Hello, RimWorld!
[script] SAMPLE add=5
[script] SAMPLE sum=10
[script] SAMPLE version=1.0.0
[script] SAMPLE info=api=sample.info, tick=1, main=True
[script] SAMPLE api_count=6
[api] sample.fail failed: System.InvalidOperationException: sample API failure
[script] SAMPLE fail_caught=false err=zz_sample_test.lua:(7,48-50): [sample.fail] InvalidOperationException: sample API failure
```

结论：

- 加载顺序无关：子模组先注册，RimLua 后初始化时已携带 6 个第三方 API。
- 字符串、整数、数组、全局常量、上下文、`api_count` 全部正确。
- C# 异常转为 Lua 错误，`pcall` 可捕获，脚本继续执行。
- 同轮冒烟测试 **32/32 PASS**（M0 的 27 项 + 扩展 API 5 项）。

完整日志：[M1-extension-log.txt](M1-extension-log.txt)
冒烟报告：[M1-smoke-report.txt](M1-smoke-report.txt)

## 4. 文件清单

```text
Source/RimLua/PublicApi/LuaApiAttributes.cs     [LuaModule]/[LuaFunction]/[LuaGlobal]
Source/RimLua/PublicApi/LuaApiContext.cs        上下文
Source/RimLua/PublicApi/LuaApiException.cs      异常类型
Source/RimLua/Internal/LuaApiRegistry.cs        注册表/冲突/路径/构建
Source/RimLua/Internal/LuaValueConverter.cs     类型编解码
Source/RimLua/PublicApi/RimLuaApi.cs            公共入口
Examples/SampleExtension/                       示例子模组（可编译）
Docs/EXTENSION_API.md                           扩展指南
```

## 5. 当前限制（明确不做）

- 事件/定时器/持久化/控制台（按用户要求暂缓）。
- 游戏对象 API（`rim.game/map/pawn`）未实现；子模组可自行通过 API 暴露安全数据。
- API 注销/覆盖未提供（先注册者优先）。
- API 调用耗时统计与硬上限未实现（M1 补）。
- API 只能在主线程被 Lua 调用；子模组 API 内部不得做耗时/阻塞操作。

## 6. 复现

```powershell
# 1) 构建 RimLua
powershell -File tools/build.ps1

# 2) 构建示例扩展
powershell -File Examples/SampleExtension/build-sample.ps1

# 3) 把 Examples/SampleExtension 复制到 Mods/RimLuaSample 并启用
#    （启用顺序：Harmony → RimLua Sample → RimLua 或任意顺序）

# 4) 在 <Config>/RimLua/scripts/ 放测试脚本，保存后自动执行
```
