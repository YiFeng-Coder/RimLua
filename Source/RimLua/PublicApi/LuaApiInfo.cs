using System.Collections.Generic;

namespace RimLua.Api
{
    /// <summary>已注册 API 的描述（用于文档/控制台/API dump）。</summary>
    public sealed class LuaApiInfo
    {
        public string Path;
        public string Kind;       // function / value
        public string Signature;  // 例如 "int mymod.add(int, int)"
    }

    /// <summary>单个 API 的调用统计。</summary>
    public sealed class LuaApiStat
    {
        public string Path;
        public int Calls;
        public int Failures;
        public int BudgetViolations;
        public double TotalMs;
        public double MaxMs;
        public double LastMs;
    }
}
