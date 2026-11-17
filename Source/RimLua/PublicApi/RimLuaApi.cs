using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RimLua.Api;
using RimLua.Internal;
using Verse;

namespace RimLua
{
    /// <summary>
    /// 第三方 mod 的 Lua API 注册入口 + 运行时查询/控制入口。
    /// 注册路径相对于 rim 根表，例如 "mymod.greet" 对应 Lua 的 rim.mymod.greet。
    /// </summary>
    public static class RimLuaApi
    {
        public const int ApiVersion = RimLuaInfo.ApiVersion;
        public const string Version = RimLuaInfo.Version;

        public static bool IsReady { get { return LuaRuntime.Initialized; } }

        public static string LastInitializationError { get { return LuaRuntime.InitError; } }

        public static int RegisteredApiCount { get { return LuaApiRegistry.Count; } }

        public static string ScriptsDirectory { get { return LuaScriptLoader.LocalScriptDir; } }

        public static string SmokeReportPath
        {
            get { return Path.Combine(GenFilePaths.ConfigFolderPath, "RimLua", "smoke-report.txt"); }
        }

        public static string ApiDumpPath
        {
            get { return Path.Combine(GenFilePaths.ConfigFolderPath, "RimLua", "api-dump.md"); }
        }

        // ---------- 注册 ----------

        public static bool RegisterFunction(string path, Delegate function)
        {
            return LuaApiRegistry.RegisterFunction(path, function);
        }

        public static bool RegisterGlobal(string path, object value)
        {
            return LuaApiRegistry.RegisterGlobal(path, value);
        }

        public static int RegisterModule(Type moduleType)
        {
            return LuaApiRegistry.RegisterModule(moduleType);
        }

        public static int RegisterModule(object moduleInstance)
        {
            return LuaApiRegistry.RegisterModule(moduleInstance);
        }

        public static int RegisterAllModules(Assembly assembly)
        {
            return LuaApiRegistry.RegisterAllModules(assembly);
        }

        public static void WhenReady(Action callback)
        {
            LuaApiRegistry.WhenReady(callback);
        }

        // ---------- 查询 ----------

        public static List<LuaApiInfo> GetRegisteredApis()
        {
            return LuaApiRegistry.GetInfos();
        }

        public static List<LuaApiStat> GetApiStats()
        {
            return LuaApiStats.Snapshot();
        }

        public static List<LuaScriptInfo> GetLoadedScripts()
        {
            return LuaScriptLoader.Scripts;
        }

        public static bool TryGetScript(string scriptId, out LuaScriptInfo info)
        {
            return LuaScriptLoader.TryGetScript(scriptId, out info);
        }

        public static string GetScriptStateJson(string scriptId)
        {
            string json;
            return LuaStateStore.TryGetStateJson(scriptId, string.Empty, out json) ? json : null;
        }

        // ---------- C# → Lua ----------

        /// <summary>调用某个已加载脚本里的全局函数（必须主线程）。</summary>
        public static LuaCallResult CallScript(string scriptId, string functionName, params object[] args)
        {
            if (!LuaRuntime.Initialized)
            {
                return LuaCallResult.Failure("RimLua runtime is not initialized");
            }
            if (!RimLuaThread.IsMainThread)
            {
                return LuaCallResult.Failure("CallScript must be invoked on the main thread");
            }

            LuaScriptInfo info;
            if (!LuaScriptLoader.TryGetScript(scriptId, out info) || info.Script == null)
            {
                return LuaCallResult.Failure("script not found or failed to load: " + scriptId);
            }

            int wall = RimLuaMod.Settings != null ? RimLuaMod.Settings.scriptWallClockBudgetMs : 50;
            int slice = RimLuaMod.Settings != null ? RimLuaMod.Settings.sliceInstructionBudget : 5000;
            LuaCallResult result = LuaRuntime.CallFunction(info.Script, functionName, args, wall, slice);
            if (result.Ok)
            {
                LuaStateStore.Capture(scriptId, info.Script);
            }
            return result;
        }

        // ---------- API dump ----------

        public static string DumpApiMarkdown()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# RimLua API dump");
            builder.AppendLine();
            builder.AppendLine("- runtime: " + Version + " (api " + ApiVersion + ")");
            builder.AppendLine("- game: " + SafeGameVersion());
            builder.AppendLine("- generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine("## Built-in");
            builder.AppendLine();
            builder.AppendLine("| path | kind | signature |");
            builder.AppendLine("| --- | --- | --- |");
            builder.AppendLine("| rim.version | value | string |");
            builder.AppendLine("| rim.api_version | value | int |");
            builder.AppendLine("| rim.script_id | value | string |");
            builder.AppendLine("| rim.api_count | value | int |");
            builder.AppendLine("| rim.add | function | int rim.add(int, int) |");
            builder.AppendLine("| rim.ticks | function | int rim.ticks() |");
            builder.AppendLine("| rim.is_main_thread | function | bool rim.is_main_thread() |");
            builder.AppendLine("| log.info / log.warn / log.error | function | void log.x(string) |");
            builder.AppendLine();
            List<LuaApiInfo> infos = GetRegisteredApis();
            builder.AppendLine("## Registered (" + infos.Count + ")");
            builder.AppendLine();
            builder.AppendLine("| path | kind | signature |");
            builder.AppendLine("| --- | --- | --- |");
            foreach (LuaApiInfo info in infos)
            {
                builder.AppendLine("| " + info.Path + " | " + info.Kind + " | " + info.Signature + " |");
            }
            builder.AppendLine();
            List<LuaApiStat> stats = GetApiStats();
            if (stats.Count > 0)
            {
                builder.AppendLine("## Call stats");
                builder.AppendLine();
                builder.AppendLine("| path | calls | failures | budget | total ms | max ms |");
                builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
                foreach (LuaApiStat stat in stats)
                {
                    builder.AppendLine("| " + stat.Path + " | " + stat.Calls + " | " + stat.Failures + " | " +
                                       stat.BudgetViolations + " | " + stat.TotalMs.ToString("F1") + " | " +
                                       stat.MaxMs.ToString("F1") + " |");
                }
            }
            return builder.ToString();
        }

        public static string WriteApiDump()
        {
            string path = ApiDumpPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, DumpApiMarkdown(), Encoding.UTF8);
            return path;
        }

        private static string SafeGameVersion()
        {
            try { return RimWorld.VersionControl.CurrentVersionStringWithRev; }
            catch { return "unknown"; }
        }
    }
}
