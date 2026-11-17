using System;
using System.Collections.Generic;
using System.IO;
using RimLua.Api;
using Verse;

namespace RimLua
{
    /// <summary>
    /// 控制台服务：UI 只负责输入与显示，所有脚本执行都排队到主线程 GameComponentUpdate。
    /// 支持命令（以 : 开头）与 Lua 代码（= 开头表示表达式）。
    /// </summary>
    public static class LuaConsoleService
    {
        private const int MaxLines = 300;
        private static readonly object _sync = new object();
        private static readonly Queue<string> _pending = new Queue<string>();
        private static readonly List<string> _lines = new List<string>();

        public static bool HasPending
        {
            get { lock (_sync) { return _pending.Count > 0; } }
        }

        public static void Submit(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;
            lock (_sync) { _pending.Enqueue(input); }
        }

        public static List<string> GetLines()
        {
            lock (_sync) { return new List<string>(_lines); }
        }

        public static void Clear()
        {
            lock (_sync) { _lines.Clear(); }
        }

        /// <summary>每帧主线程调用。</summary>
        public static void Pump()
        {
            string input;
            while (true)
            {
                lock (_sync)
                {
                    if (_pending.Count == 0) return;
                    input = _pending.Dequeue();
                }
                try
                {
                    Execute(input);
                }
                catch (Exception e)
                {
                    Add("ERROR: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        private static void Execute(string input)
        {
            string text = input.Trim();
            Add("> " + text);

            if (text.StartsWith(":"))
            {
                ExecuteCommand(text.Substring(1).Trim());
                return;
            }
            if (text.StartsWith("="))
            {
                text = "return " + text.Substring(1).Trim();
            }

            int wall = RimLuaMod.Settings != null ? RimLuaMod.Settings.scriptWallClockBudgetMs : 50;
            int slice = RimLuaMod.Settings != null ? RimLuaMod.Settings.sliceInstructionBudget : 5000;
            LuaRunResult result = LuaRuntime.ExecuteSliced(text, "console", wall, slice);
            if (result.IsSuccess)
            {
                Add("-> " + result.ReturnText + "  [" + result.ToString() + "]");
            }
            else
            {
                Add("!! " + result.Status + ": " + result.Error);
            }
        }

        private static void ExecuteCommand(string command)
        {
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string name = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            switch (name)
            {
                case "help":
                    Add("命令: :help  :list  :reload [id]  :api  :apidump  :stats  :state [id]  :smoke  :clear");
                    Add("直接输入 Lua 代码；= 开头表示表达式（自动 return）。");
                    break;
                case "clear":
                    Clear();
                    break;
                case "list":
                    ListScripts();
                    break;
                case "reload":
                    if (parts.Length > 1) ReloadOne(parts[1]);
                    else ReloadAll();
                    break;
                case "api":
                    AddApiDump();
                    break;
                case "apidump":
                    WriteApiDump();
                    break;
                case "stats":
                    DumpStats();
                    break;
                case "smoke":
                    LuaSmokeTests.RunAgain();
                    Add("已重新运行冒烟测试，结果见 " + RimLuaApi.SmokeReportPath);
                    break;
                case "state":
                    DumpState(parts.Length > 1 ? parts[1] : null);
                    break;
                default:
                    Add("未知命令: " + name + "（:help）");
                    break;
            }
        }

        private static void ListScripts()
        {
            List<LuaScriptInfo> scripts = RimLuaApi.GetLoadedScripts();
            Add("已加载脚本: " + scripts.Count);
            foreach (LuaScriptInfo script in scripts)
            {
                string status = script.LastRun != null ? script.LastRun.Status.ToString() : "not run";
                Add("  " + script.Id + "  [" + status + "]  hash=" + script.Hash);
            }
        }

        private static void ReloadAll()
        {
            int ok = LuaScriptLoader.ReloadAll();
            Add("ReloadAll: " + ok + " ok");
        }

        private static void ReloadOne(string id)
        {
            string error;
            bool ok = LuaScriptLoader.ReloadScript(id, out error);
            Add(ok ? "reload ok: " + id : "reload failed: " + error);
        }

        private static void AddApiDump()
        {
            string[] lines = RimLuaApi.DumpApiMarkdown().Split(new[] { '\n' }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                Add(line);
                if (GetLines().Count >= MaxLines - 5) break;
            }
        }

        private static void WriteApiDump()
        {
            try
            {
                string path = RimLuaApi.WriteApiDump();
                Add("api dump written: " + path);
            }
            catch (Exception e)
            {
                Add("api dump failed: " + e.Message);
            }
        }

        private static void DumpStats()
        {
            List<LuaApiStat> stats = RimLuaApi.GetApiStats();
            Add("API 调用统计: " + stats.Count + " 项");
            foreach (LuaApiStat stat in stats)
            {
                Add("  " + stat.Path + " calls=" + stat.Calls + " fail=" + stat.Failures +
                    " budget=" + stat.BudgetViolations + " avg=" +
                    (stat.Calls > 0 ? (stat.TotalMs / stat.Calls).ToString("F2") : "0") + "ms max=" +
                    stat.MaxMs.ToString("F2") + "ms");
            }
        }

        private static void DumpState(string scriptId)
        {
            List<LuaScriptInfo> scripts = RimLuaApi.GetLoadedScripts();
            if (string.IsNullOrEmpty(scriptId))
            {
                Add("用法: :state <scriptId>；脚本列表见 :list");
                return;
            }
            string json = RimLuaApi.GetScriptStateJson(scriptId);
            Add(json == null ? "无状态: " + scriptId : json);
        }

        private static void Add(string line)
        {
            lock (_sync)
            {
                _lines.Add(line);
                if (_lines.Count > MaxLines)
                {
                    _lines.RemoveRange(0, _lines.Count - MaxLines);
                }
            }
        }
    }
}
