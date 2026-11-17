using System;

namespace RimLua.Api
{
    /// <summary>C# 主动调用 Lua 函数的结果。</summary>
    public sealed class LuaCallResult
    {
        public bool Ok;
        public string ReturnText;
        public string Error;
        public double ElapsedMs;
        public int Slices;

        public static LuaCallResult Success(string returnText, double elapsedMs, int slices)
        {
            return new LuaCallResult { Ok = true, ReturnText = returnText, ElapsedMs = elapsedMs, Slices = slices };
        }

        public static LuaCallResult Failure(string error, double elapsedMs = 0, int slices = 0)
        {
            return new LuaCallResult { Ok = false, Error = error, ElapsedMs = elapsedMs, Slices = slices };
        }

        public override string ToString()
        {
            return Ok ? "Ok: " + ReturnText + " (" + ElapsedMs.ToString("F1") + " ms)" : "Error: " + Error;
        }
    }
}
