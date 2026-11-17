# RimLua

为 RimWorld 1.7（Waylith / .NET 10）提供 MoonSharp Lua 运行时与 C# 扩展 API 的模组。

## 当前能力（2026-11-17）

游戏内冒烟测试 **51/51 通过**，覆盖：

- 模组加载、MoonSharp 3.0.0.0 初始化、硬沙箱
- 无限循环切片中止、64 KiB 解析、C# 回调、主线程执行
- 本地脚本实时热加载（保存后立即执行）
- **任意子模组注册 Lua C# API**（特性扫描 / 手动注册 / 类型编解码 / 错误处理）
- **API 调用耗时统计与单次预算**（超预算返回 Lua 错误并计数）
- **脚本 state 持久化**（随存档保存/恢复，JSON）
- **游戏内控制台**（REPL、reload、list、api、apidump、stats、state）
- **C# → Lua 调用**（`RimLuaApi.CallScript`）
- **API dump**（Markdown，控制台 `:apidump`）

按用户要求，**事件/定时器暂缓**。

## 文档

- [Docs/TUTORIAL_FOR_SUBMOD_AUTHORS.md](Docs/TUTORIAL_FOR_SUBMOD_AUTHORS.md)：**子模组开发教程（建议先读）**
- [Docs/EXTENSION_API.md](Docs/EXTENSION_API.md)：第三方 Lua C# API 参考
- [Docs/CONSOLE_AND_PERSISTENCE.md](Docs/CONSOLE_AND_PERSISTENCE.md)：控制台与 state 持久化
- [Docs/CALL_LUA_AND_APIDUMP.md](Docs/CALL_LUA_AND_APIDUMP.md)：C#→Lua、API dump、耗时预算
- [Examples/SampleExtension](Examples/SampleExtension)：可编译的示例子模组
- [Docs/M2-PLATFORM-REPORT.md](Docs/M2-PLATFORM-REPORT.md)：本轮验证报告
- [Docs/M0-REPORT.md](Docs/M0-REPORT.md)、[Docs/M1-EXTENSION-API-REPORT.md](Docs/M1-EXTENSION-API-REPORT.md)：历史阶段报告

## 目录

- `About/`、`LoadFolders.xml`：模组元数据与版本目录
- `1.7/Assemblies/`：运行时 DLL（RimLua + MoonSharp）
- `1.7/Lua/core/`：内置 Lua 核心库
- `Source/RimLua/`：C# 源码
- `tools/build.ps1`：csc 直编构建脚本
- `Examples/SampleExtension/`：示例子模组（含独立构建脚本）
- `ThirdParty/MoonSharp/`：MoonSharp netstandard2.0 DLL 与 LICENSE
- `Docs/`：计划、指南、报告与证据

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File tools/build.ps1
powershell -ExecutionPolicy Bypass -File Examples/SampleExtension/build-sample.ps1
```

说明：本机当前环境 MSBuild 子进程无法创建临时目录，因此构建脚本使用 csc 直编；
`Source/RimLua/RimLua.csproj` 保留给正常 MSBuild/Visual Studio 环境。

## 使用

1. 启用 Harmony 与 RimLua，进入游戏。
2. 配置目录：`<Config>/RimLua/`
   - `scripts/*.lua`：本地脚本（保存即热加载）
   - `smoke-report.txt`：冒烟报告
   - `api-dump.md`：API dump
3. Mod 设置页可打开 Lua 控制台，调整预算/热加载开关。

```lua
log.info("hello " .. rim.version .. ", tick=" .. rim.ticks())
state.count = (state.count or 0) + 1
```

> 注意：能写 `Config/RimLua/scripts` 的程序即可让游戏执行 Lua，v1 仅对本地可信脚本开放。

## License

- RimLua 本体：MIT（见 [LICENSE](LICENSE)）
- MoonSharp：BSD-3-Clause（见 [ThirdParty/MoonSharp/LICENSE](ThirdParty/MoonSharp/LICENSE)）
