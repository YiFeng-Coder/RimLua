using System;
using RimLua;
using RimLua.Api;
using Verse;

namespace RimLuaSample
{
    /// <summary>示例 API：通过 [LuaModule]/[LuaFunction]/[LuaGlobal] 暴露给 Lua。</summary>
    [LuaModule("sample")]
    public static class SampleLuaApi
    {
        [LuaGlobal("version")]
        public const string Version = "1.0.0";

        [LuaFunction("greet")]
        public static string Greet(string name)
        {
            return "Hello, " + name + "!";
        }

        [LuaFunction("add")]
        public static int Add(int a, int b)
        {
            return a + b;
        }

        [LuaFunction("sum")]
        public static int Sum(int[] values)
        {
            int total = 0;
            foreach (int value in values) total += value;
            return total;
        }

        [LuaFunction("info")]
        public static string Info(LuaApiContext context)
        {
            return "api=" + context.ApiName + ", tick=" + context.GameTick + ", main=" + context.IsMainThread;
        }

        [LuaFunction("fail")]
        public static void Fail()
        {
            throw new InvalidOperationException("sample API failure");
        }
    }

    /// <summary>示例 Mod 入口：在构造时注册本模组的 Lua API。</summary>
    public class RimLuaSampleMod : Mod
    {
        public RimLuaSampleMod(ModContentPack content) : base(content)
        {
            int count = RimLuaApi.RegisterModule(typeof(SampleLuaApi));
            Log.Message("[RimLuaSample] registered " + count + " Lua API entries, rimluaReady=" + RimLuaApi.IsReady);
        }
    }
}
