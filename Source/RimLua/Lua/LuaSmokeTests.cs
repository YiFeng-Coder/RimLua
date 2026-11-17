using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using RimLua.Api;
using Verse;

namespace RimLua
{
    /// <summary>
    /// M0 冒烟测试：验证 MoonSharp 加载、硬沙箱、协程切片、解析成本、回调、主线程、实时热加载。
    /// 结果写入 Config/RimLua/smoke-report.txt 并输出到 Player.log。
    /// </summary>
    public static class LuaSmokeTests
    {
        private const string LogTag = RimLuaInfo.LogPrefix + "[smoke]";
        private static readonly List<string> _lines = new List<string>();
        private static bool _begun;
        private static bool _finished;
        private static int _ticks;
        private static string _reportPath;
        private static int _pass;
        private static int _fail;

        public static void Begin()
        {
            if (_begun) return;
            _begun = true;

            try
            {
                _reportPath = Path.Combine(GenFilePaths.ConfigFolderPath, "RimLua", "smoke-report.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
            }
            catch (Exception e)
            {
                Log.Warning(LogTag + " cannot prepare report file: " + e.Message);
            }

            Add("RimLua smoke tests");
            Add("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Add("game=" + VersionInfo.GameVersionText());
            Add("mainThread=" + RimLuaThread.IsMainThread);
            Add("");

            Run("moonSharp-load", TestLoad);
            Run("sandbox-globals", TestSandbox);
            Run("coroutine-slice", TestSlice);
            Run("parse-64k", TestParse);
            Run("callback", TestCallback);
            Run("main-thread", TestMainThread);
            Run("thirdparty-api", TestThirdPartyApi);
            Run("api-budget", TestApiBudget);
            Run("state-persistence", TestStatePersistence);
            Run("csharp-call-lua", TestCSharpCallLua);
            Run("console-service", TestConsoleService);
            Run("api-dump", TestApiDump);

            string selfTest = LuaScriptLoader.WriteSelfTestScript();
            Add("");
            Add("live-reload: waiting for watcher to execute " + LuaScriptLoader.SelfTestFileName);
            if (selfTest == null) Fail("live-reload", "script directory unavailable");

            Flush();
        }

        /// <summary>重新运行冒烟测试（供设置页/控制台调用；主线程）。</summary>
        public static void RunAgain()
        {
            _begun = false;
            _finished = false;
            _pass = 0;
            _fail = 0;
            _ticks = 0;
            _lines.Clear();
            Begin();
        }

        public static void Pump()
        {
            if (!_begun || _finished) return;
            _ticks++;

            if (LuaScriptLoader.SelfTestSeen)
            {
                string detail = LuaScriptLoader.SelfTestDetail ?? string.Empty;
                if (detail.StartsWith("Ok", StringComparison.Ordinal))
                {
                    Pass("live-reload", detail);
                }
                else
                {
                    Fail("live-reload", detail);
                }
                Finish();
                return;
            }

            if (_ticks > 900)
            {
                Fail("live-reload", "not observed after " + _ticks + " ticks; lastError=" + LuaScriptLoader.LastError);
                Finish();
            }
        }

        private static void Finish()
        {
            if (_finished) return;
            _finished = true;
            Add("");
            Add("RESULT: " + (_fail == 0 ? "PASS" : "FAIL") + " (" + _pass + " passed, " + _fail + " failed)");
            Add("moonSharp=" + LuaRuntime.MoonSharpVersion + ", reports=" + _reportPath);
            Flush();
            Log.Message(LogTag + " finished: " + (_fail == 0 ? "PASS" : "FAIL") + ", report=" + _reportPath);

            if (RimLuaMod.Settings != null && RimLuaMod.Settings.runSmokeTestsOnLoad)
            {
                LuaScriptLoader.WriteSelfTestScript();
            }
        }

        private static void TestLoad()
        {
            Check(LuaRuntime.Initialized, "initialized=" + LuaRuntime.Initialized + ", error=" + LuaRuntime.InitError);
            Check(LuaRuntime.Sandbox != null, "sandbox instance created");
            Check(LuaRuntime.MoonSharpVersion != null, "MoonSharp version=" + LuaRuntime.MoonSharpVersion);
        }

        private static void TestSandbox()
        {
            var script = LuaRuntime.CreateScript();
            string[] forbidden = { "io", "os", "require", "load", "loadfile", "dofile", "loadstring", "debug" };
            foreach (string name in forbidden)
            {
                Check(script.Globals.Get(name).IsNil(), "global '" + name + "' is nil");
            }
            Check(!script.Globals.Get("pcall").IsNil(), "pcall present (ErrorHandling enabled)");
            Check(!script.Globals.Get("setmetatable").IsNil(), "setmetatable present (Metatables enabled)");
            Check(script.Globals.Get("coroutine").IsNil(), "coroutine global absent (host-driven slicing)");
        }

        private static void TestSlice()
        {
            const string code = "local i = 0 while true do i = i + 1 end";
            LuaRunResult result = LuaRuntime.ExecuteSliced(code, "smoke_slice", 20, 5000);
            Check(result.Status == LuaRunStatus.Aborted,
                "infinite loop aborted: " + result + ", status=" + result.Status);
            Check(result.Slices > 1, "slices observed=" + result.Slices);
            Check(result.ElapsedMs < 1000, "abort returned in " + result.ElapsedMs.ToString("F1") + " ms");
        }

        private static void TestParse()
        {
            var sb = new StringBuilder();
            int index = 0;
            while (sb.Length < 64 * 1024)
            {
                sb.Append("local a").Append(index).Append(" = ").Append(index).Append("\n");
                index++;
            }
            string code = sb.ToString();
            var script = LuaRuntime.CreateScript();
            var watch = Stopwatch.StartNew();
            try
            {
                script.LoadString(code, null, "smoke_parse");
                watch.Stop();
                Check(watch.ElapsedMilliseconds < 500,
                    "parse " + code.Length + " chars in " + watch.ElapsedMilliseconds + " ms");
            }
            catch (Exception e)
            {
                watch.Stop();
                Fail("parse-64k", "parse failed: " + e.Message);
            }
        }

        private static void TestCallback()
        {
            LuaRunResult result = LuaRuntime.ExecuteSliced("return rim.add(20, 22)", "smoke_callback", 50, 5000);
            Check(result.IsSuccess && result.ReturnText == "42", "rim.add(20,22) returned " + result.ReturnText + " (" + result + ")");
        }

        private static void TestMainThread()
        {
            Check(RimLuaThread.IsMainThread, "smoke tests executed on main thread");
        }

        private static void TestThirdPartyApi()
        {
            // 运行时初始化后再注册，验证“注册后立即生效”（注册表在每次建脚本时重建）。
            RimLuaApi.RegisterFunction("_selftest.add", (Func<int, int, int>)((a, b) => a + b));
            RimLuaApi.RegisterFunction("_selftest.greet", (Func<string, string>)(name => "hi " + name));
            RimLuaApi.RegisterGlobal("_selftest.version", "m0");

            LuaRunResult add = LuaRuntime.ExecuteSliced("return rim._selftest.add(20, 22)", "smoke_api_add", 50, 5000);
            Check(add.IsSuccess && add.ReturnText == "42", "third-party function -> " + add.ReturnText + " (" + add + ")");

            LuaRunResult greet = LuaRuntime.ExecuteSliced("return rim._selftest.greet('lua')", "smoke_api_greet", 50, 5000);
            Check(greet.IsSuccess && greet.ReturnText == "hi lua", "string marshal -> " + greet.ReturnText + " (" + greet + ")");

            LuaRunResult global = LuaRuntime.ExecuteSliced("return rim._selftest.version", "smoke_api_global", 50, 5000);
            Check(global.IsSuccess && global.ReturnText == "m0", "global -> " + global.ReturnText + " (" + global + ")");

            LuaRunResult bad = LuaRuntime.ExecuteSliced("return rim._selftest.add('x', 2)", "smoke_api_bad", 50, 5000);
            Check(bad.Status == LuaRunStatus.Error, "bad argument rejected -> " + bad.Status + " (" + bad.Error + ")");
        }

        private static void TestApiBudget()
        {
            RimLuaApi.RegisterFunction("_selftest.slow", (Func<int, int>)(milliseconds =>
            {
                Thread.Sleep(milliseconds);
                return milliseconds;
            }));

            double oldBudget = RimLuaMod.Settings != null ? RimLuaMod.Settings.apiCallBudgetMs : 10.0;
            try
            {
                if (RimLuaMod.Settings != null) RimLuaMod.Settings.apiCallBudgetMs = 5.0;
                LuaRunResult result = LuaRuntime.ExecuteSliced("return rim._selftest.slow(25)", "smoke_api_budget", 300, 5000);
                Check(result.Status == LuaRunStatus.Error && result.Error != null && result.Error.Contains("budget"),
                    "slow API rejected -> " + result.Status + " (" + result.Error + ")");

                List<LuaApiStat> stats = RimLuaApi.GetApiStats();
                bool violated = false;
                foreach (LuaApiStat stat in stats)
                {
                    if (stat.Path == "_selftest.slow" && stat.BudgetViolations > 0) violated = true;
                }
                Check(violated, "budget violation recorded in stats");
            }
            finally
            {
                if (RimLuaMod.Settings != null) RimLuaMod.Settings.apiCallBudgetMs = oldBudget;
            }
        }

        private static void TestStatePersistence()
        {
            string path = LuaScriptLoader.WriteCallTestScript("_rimlua_state_test.lua",
                "state.count = (state.count or 0) + 1\n");
            if (path == null)
            {
                Fail("state-persistence", "script directory unavailable");
                return;
            }

            const string id = "local/_rimlua_state_test.lua";
            LuaRunResult first = LuaScriptLoader.LoadFileNow(path, "local", false);
            Check(first.IsSuccess, "first run -> " + first);

            LuaRunResult second = LuaScriptLoader.LoadFileNow(path, "local", true);
            Check(second.IsSuccess, "second run -> " + second);

            string json = RimLuaApi.GetScriptStateJson(id);
            Check(json != null && json.Contains("2"), "state after two runs -> " + json);

            string saved = LuaStateStore.SerializeForSave();
            Check(!string.IsNullOrEmpty(saved) && saved.Contains("_rimlua_state_test.lua"), "serialized state contains script id");
            LuaStateStore.LoadFrom(saved);
            string json2 = RimLuaApi.GetScriptStateJson(id);
            Check(json2 != null && json2.Contains("2"), "state survives save/load roundtrip -> " + json2);
        }

        private static void TestCSharpCallLua()
        {
            string path = LuaScriptLoader.WriteCallTestScript("_rimlua_call_test.lua",
                "function rimlua_test_add(a, b) return a + b end\n" +
                "function rimlua_test_fail() error('boom') end\n");
            if (path == null)
            {
                Fail("csharp-call-lua", "script directory unavailable");
                return;
            }

            const string id = "local/_rimlua_call_test.lua";
            LuaRunResult load = LuaScriptLoader.LoadFileNow(path, "local", false);
            Check(load.IsSuccess, "call test script loaded -> " + load);

            LuaCallResult ok = RimLuaApi.CallScript(id, "rimlua_test_add", 20, 22);
            Check(ok.Ok && ok.ReturnText == "42", "CallScript add -> " + ok);

            LuaCallResult missing = RimLuaApi.CallScript(id, "missing_function");
            Check(!missing.Ok, "CallScript missing function rejected -> " + missing.Error);

            LuaCallResult fail = RimLuaApi.CallScript(id, "rimlua_test_fail");
            Check(!fail.Ok && fail.Error != null && fail.Error.Contains("boom"), "CallScript error propagated -> " + fail.Error);
        }

        private static void TestConsoleService()
        {
            LuaConsoleService.Clear();
            LuaConsoleService.Submit("= 20 + 22");
            LuaConsoleService.Pump();
            List<string> lines = LuaConsoleService.GetLines();
            bool found = false;
            foreach (string line in lines)
            {
                if (line.Contains("-> 42")) found = true;
            }
            Check(found, "console expression -> " + (lines.Count > 0 ? lines[lines.Count - 1] : "<empty>"));
        }

        private static void TestApiDump()
        {
            string markdown = RimLuaApi.DumpApiMarkdown();
            Check(markdown.Contains("_selftest.add") && markdown.Contains("## Registered"),
                "api dump contains registered API and section");
            string path = RimLuaApi.WriteApiDump();
            Check(File.Exists(path), "api dump written -> " + path);
        }

        private static void Run(string name, Action test)
        {
            int failBefore = _fail;
            try
            {
                test();
                if (_fail == failBefore)
                {
                    Pass(name, "ok");
                }
            }
            catch (Exception e)
            {
                Fail(name, e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Check(bool condition, string detail)
        {
            string caller = "unknown";
            var frame = new StackTrace(1, false).GetFrame(0);
            if (frame != null && frame.GetMethod() != null) caller = frame.GetMethod().Name;
            if (condition) Pass(caller, detail); else Fail(caller, detail);
        }

        private static void Pass(string name, string detail)
        {
            _pass++;
            Add("PASS " + name + ": " + detail);
        }

        private static void Fail(string name, string detail)
        {
            _fail++;
            Add("FAIL " + name + ": " + detail);
        }

        private static void Add(string line)
        {
            _lines.Add(line);
            Log.Message(LogTag + " " + line);
        }

        private static void Flush()
        {
            if (string.IsNullOrEmpty(_reportPath)) return;
            try
            {
                File.WriteAllLines(_reportPath, _lines.ToArray());
            }
            catch (Exception e)
            {
                Log.Warning(LogTag + " cannot write report: " + e.Message);
            }
        }

        private static class VersionInfo
        {
            public static string GameVersionText()
            {
                try { return RimWorld.VersionControl.CurrentVersionStringWithRev; }
                catch { return "unknown"; }
            }
        }
    }
}