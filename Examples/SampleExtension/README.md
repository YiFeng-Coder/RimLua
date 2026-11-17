# RimLua Sample Extension

演示“任意子模组通过 RimLua 注册 Lua C# API”。

## 结构

- `Source/RimLuaSample/SampleLuaApi.cs`：[LuaModule("sample")] + [LuaFunction]/[LuaGlobal]
- `Source/RimLuaSample/RimLuaSampleMod.cs`：Mod 构造时调用 `RimLuaApi.RegisterModule`
- `build-sample.ps1`：csc 直编，输出 `Assemblies/RimLuaSample.dll`
- `About/About.xml`：packageId `Feng.RimLua.Sample`

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File build-sample.ps1
```

## Lua 侧

```lua
log.info(rim.sample.greet("RimWorld"))   -- Hello, RimWorld!
log.info(rim.sample.add(2, 3))           -- 5
log.info(rim.sample.sum({1,2,3,4}))      -- 10
log.info(rim.sample.version)             -- 1.0.0
log.info(rim.sample.info())              -- api=..., tick=..., main=true
```

把本目录复制到 RimWorld 的 `Mods/` 下并在 mod 列表中启用即可（无需与 RimLua 保持特定加载顺序）。
