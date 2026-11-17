using System;
using System.Diagnostics;
using MoonSharp.Interpreter;
using RimLua.Api;
using RimLua.Internal;
using Verse;

namespace RimLua
{
    public enum LuaRunStatus
    {
        Ok,
        Error,
        Aborted
    }

    /// <summary>一次脚本执行的观测结果。</summary>
    public sealed class LuaRunResult
    {
        public LuaRunStatus Status;
        public string Error;
        public int Slices;
        public double ElapsedMs;
        public string ReturnText;

        public bool IsSuccess { get { return Status == LuaRunStatus.Ok; } }

        public override string ToString()
        {
            return Status + " in " + ElapsedMs.ToString("F1") + " ms, slices=" + Slices +
                   (string.IsNullOrEmpty(Error) ? string.Empty : ", error=" + Error);
        }
    }

    /// <summary>
    /// MoonSharp 宿主：硬沙箱、协程切片、主线程约束、脚本状态注入/C#→Lua 调用。
    /// </summary>
    public static class LuaRuntime
    {
        private static readonly object _initLock = new object();
        private static Script _sandbox;

        public static bool Initialized { get; private set; }
        public static string InitError { get; private set; }
        public static Version MoonSharpVersion { get; private set; }
        public static Script Sandbox { get { return _sandbox; } }

        public static void EnsureInitialized()
        {
            if (Initialized) return;
            lock (_initLock)
            {
                if (Initialized) return;
                try
                {
                    MoonSharpVersion = typeof(Script).Assembly.GetName().Version;
                    Script.WarmUp();
                    _sandbox = CreateScript();
                    InstallGlobals(_sandbox, null);

                    DynValue probe = _sandbox.DoString("return 1 + 2");
                    if (probe.Type != DataType.Number || Math.Abs(probe.Number - 3.0) > 0.0001)
                    {
                        throw new InvalidOperationException("sandbox probe returned " + probe.ToPrintString());
                    }

                    Initialized = true;
                    Log.Message(RimLuaInfo.LogPrefix + " MoonSharp " + MoonSharpVersion +
                                " initialized on main thread=" + RimLuaThread.IsMainThread +
                                ", third-party APIs=" + LuaApiRegistry.Count);
                    LuaApiRegistry.NotifyReady();
                }
                catch (Exception e)
                {
                    InitError = e.ToString();
                    Log.Error(RimLuaInfo.LogPrefix + " initialization failed: " + e);
                }
            }
        }

        public static Script CreateScript()
        {
            CoreModules modules = CoreModules.Preset_HardSandbox | CoreModules.ErrorHandling | CoreModules.Metatables;
            var script = new Script(modules);
            script.Options.DebugPrint = text => Log.Message(RimLuaInfo.LogPrefix + "[lua] " + text);
            script.Options.CheckThreadAccess = true;
            return script;
        }

        /// <summary>注入宿主 API 与 state 表（scriptId 为空时 state 不持久化）。</summary>
        public static void InstallGlobals(Script script, string scriptId)
        {
            var rim = new Table(script);
            rim["version"] = RimLuaInfo.Version;
            rim["api_version"] = RimLuaInfo.ApiVersion;
            rim["script_id"] = scriptId ?? "<anonymous>";
            rim["add"] = DynValue.NewCallback(
                (ctx, args) => DynValue.NewNumber(GetNumber(args, 0) + GetNumber(args, 1)), "rim.add");
            rim["ticks"] = DynValue.NewCallback((ctx, args) =>
            {
                if (Current.Game == null) return DynValue.NewNumber(0);
                var tickManager = Find.TickManager;
                return DynValue.NewNumber(tickManager != null ? tickManager.TicksGame : 0);
            }, "rim.ticks");
            rim["is_main_thread"] = DynValue.NewCallback(
                (ctx, args) => DynValue.NewBoolean(RimLuaThread.IsMainThread), "rim.is_main_thread");
            rim["api_count"] = LuaApiRegistry.Count;

            LuaApiRegistry.BuildInto(script, rim);
            script.Globals["rim"] = DynValue.NewTable(rim);

            var log = new Table(script);
            log["info"] = MakeLogCallback(Log.Message);
            log["warn"] = MakeLogCallback(Log.Warning);
            log["error"] = MakeLogCallback(Log.Error);
            script.Globals["log"] = DynValue.NewTable(log);

            if (scriptId != null)
            {
                LuaStateStore.Apply(scriptId, script);
            }
            else
            {
                script.Globals["state"] = DynValue.NewTable(new Table(script));
            }
        }

        /// <summary>在给定 Script 上执行一段代码（切片 + 墙钟预算）。</summary>
        public static LuaRunResult RunChunk(Script script, string code, string chunkName, int wallClockBudgetMs, int sliceInstructionBudget)
        {
            var result = new LuaRunResult();
            var watch = Stopwatch.StartNew();
            try
            {
                DynValue chunk = script.LoadString(code, null, chunkName);
                Coroutine coroutine = script.CreateCoroutine(chunk).Coroutine;
                coroutine.AutoYieldCounter = Math.Max(100, sliceInstructionBudget);

                DynValue current = coroutine.Resume();
                result.Slices = 1;
                while (current.Type == DataType.YieldRequest && watch.ElapsedMilliseconds < wallClockBudgetMs)
                {
                    current = coroutine.Resume();
                    result.Slices++;
                }

                if (current.Type == DataType.YieldRequest)
                {
                    result.Status = LuaRunStatus.Aborted;
                    result.Error = "wall-clock budget exceeded (" + wallClockBudgetMs + " ms)";
                }
                else
                {
                    result.Status = LuaRunStatus.Ok;
                    result.ReturnText = current.ToPrintString();
                }
            }
            catch (Exception e)
            {
                result.Status = LuaRunStatus.Error;
                result.Error = e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                watch.Stop();
                result.ElapsedMs = watch.Elapsed.TotalMilliseconds;
            }
            return result;
        }

        /// <summary>创建独立脚本并执行（控制台/冒烟用，不持久化 state）。</summary>
        public static LuaRunResult ExecuteSliced(string code, string chunkName, int wallClockBudgetMs, int sliceInstructionBudget)
        {
            Script script = CreateScript();
            InstallGlobals(script, null);
            return RunChunk(script, code, chunkName, wallClockBudgetMs, sliceInstructionBudget);
        }

        /// <summary>C# 主动调用某个脚本里的全局函数（主线程；带切片与预算）。</summary>
        public static LuaCallResult CallFunction(Script script, string functionName, object[] args, int wallClockBudgetMs, int sliceInstructionBudget)
        {
            var watch = Stopwatch.StartNew();
            int slices = 0;
            try
            {
                DynValue function = script.Globals.Get(functionName);
                if (function == null || function.Type != DataType.Function)
                {
                    return LuaCallResult.Failure("function not found: " + functionName);
                }

                int count = args != null ? args.Length : 0;
                DynValue[] callArgs = new DynValue[count];
                for (int i = 0; i < count; i++)
                {
                    callArgs[i] = LuaValueConverter.FromClr(script, args[i], "call:" + functionName);
                }

                Coroutine coroutine = script.CreateCoroutine(function).Coroutine;
                coroutine.AutoYieldCounter = Math.Max(100, sliceInstructionBudget);
                DynValue current = coroutine.Resume(callArgs);
                slices = 1;
                while (current.Type == DataType.YieldRequest && watch.ElapsedMilliseconds < wallClockBudgetMs)
                {
                    current = coroutine.Resume();
                    slices++;
                }

                watch.Stop();
                if (current.Type == DataType.YieldRequest)
                {
                    return LuaCallResult.Failure("wall-clock budget exceeded (" + wallClockBudgetMs + " ms)", watch.Elapsed.TotalMilliseconds, slices);
                }
                return LuaCallResult.Success(current.ToPrintString(), watch.Elapsed.TotalMilliseconds, slices);
            }
            catch (Exception e)
            {
                watch.Stop();
                return LuaCallResult.Failure(e.GetType().Name + ": " + e.Message, watch.Elapsed.TotalMilliseconds, slices);
            }
        }

        private static DynValue MakeLogCallback(Action<string> sink)
        {
            return DynValue.NewCallback((ctx, args) =>
            {
                sink(RimLuaInfo.LogPrefix + "[script] " + JoinArguments(args));
                return DynValue.Nil;
            }, "log");
        }

        private static string JoinArguments(CallbackArguments args)
        {
            if (args.Count <= 0) return string.Empty;
            string text = args[0].ToPrintString();
            for (int i = 1; i < args.Count; i++) text += " " + args[i].ToPrintString();
            return text;
        }

        private static double GetNumber(CallbackArguments args, int index)
        {
            if (index >= args.Count) return 0;
            try { return args[index].Number; }
            catch { return 0; }
        }
    }
}
