using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLua
{
    /// <summary>Mod 入口 + 设置页（分组、滚动、自动保存）。</summary>
    public class RimLuaMod : Mod
    {
        public static RimLuaMod Instance { get; private set; }
        public static RimLuaSettings Settings { get; private set; }

        private Vector2 _scroll;
        private float _contentHeight = 1100f;
        private bool _dirty;
        private float _lastChangeAt;

        private string _sliceBuffer;
        private string _wallBuffer;
        private string _apiBudgetBuffer;
        private string _apiWarnBuffer;
        private string _stateKbBuffer;

        public RimLuaMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RimLuaSettings>();
            Log.Message(RimLuaInfo.LogPrefix + " loaded v" + RimLuaInfo.Version +
                        " (api " + RimLuaInfo.ApiVersion + "), root=" + (content != null ? content.RootDir : "<null>"));
        }

        public static string ModRootDir
        {
            get { return Instance != null && Instance.Content != null ? Instance.Content.RootDir : null; }
        }

        public override string SettingsCategory()
        {
            return "RimLua";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (Settings == null) Settings = GetSettings<RimLuaSettings>();
            EnsureBuffers();

            const float footerHeight = 44f;
            Rect scrollRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - footerHeight);
            float viewWidth = scrollRect.width - 20f;
            float viewHeight = Mathf.Max(scrollRect.height, _contentHeight);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

            int hashBefore = SettingsHash();

            Widgets.BeginScrollView(scrollRect, ref _scroll, viewRect, true);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // ---------- 头部 / 概览 ----------
            listing.Label("<b>RimLua 设置</b>");
            listing.Label("版本 " + RimLuaInfo.Version + "  ·  ApiVersion " + RimLuaInfo.ApiVersion +
                          "  ·  MoonSharp " + (LuaRuntime.MoonSharpVersion != null ? LuaRuntime.MoonSharpVersion.ToString() : "未初始化"));
            listing.Label("<color=#9A9A9A>修改后约 0.6 秒自动保存；也可用底部按钮手动保存。</color>");
            listing.GapLine(8f);

            // ---------- 常用操作 ----------
            listing.Label("<b>常用操作</b>");
            DrawTwoButtons(listing,
                "打开 Lua 控制台", () => Find.WindowStack.Add(new Window_LuaConsole()),
                "生成 API dump", GenerateApiDump);
            DrawTwoButtons(listing,
                "打开配置目录", OpenConfigFolder,
                "重新运行冒烟测试", () => LuaConsoleService.Submit(":smoke"));
            listing.GapLine(8f);

            // ---------- 脚本 ----------
            listing.Label("<b>脚本</b>");
            listing.Label("<color=#9A9A9A>本地脚本位于 Config/RimLua/scripts；保存后会热加载。</color>");
            bool changed = false;
            bool boxChanged;
            Settings.enableLocalScripts = CheckboxRow(listing, "启用本地脚本", Settings.enableLocalScripts, out boxChanged);
            changed |= boxChanged;
            Settings.autoReloadLocalScripts = CheckboxRow(listing, "自动热加载（保存后立即执行）", Settings.autoReloadLocalScripts, out boxChanged);
            changed |= boxChanged;
            Settings.runSmokeTestsOnLoad = CheckboxRow(listing, "启动时运行冒烟测试（开发用）", Settings.runSmokeTestsOnLoad, out boxChanged);
            changed |= boxChanged;
            string scriptsDir = LuaScriptLoader.LocalScriptDir;
            listing.Label("目录：" + (string.IsNullOrEmpty(scriptsDir) ? "<尚未初始化>" : scriptsDir));
            listing.GapLine(8f);

            // ---------- 执行预算 ----------
            listing.Label("<b>执行预算</b>");
            listing.Label("<color=#9A9A9A>控制单个脚本最多执行多久、多久让出一次主线程。</color>");
            NumericIntRow(listing, "每片指令数（100–100000）", ref Settings.sliceInstructionBudget,
                ref _sliceBuffer, 100, 100000);
            NumericIntRow(listing, "单次脚本墙钟预算 ms（1–60000）", ref Settings.scriptWallClockBudgetMs,
                ref _wallBuffer, 1, 60000);
            listing.GapLine(8f);

            // ---------- 第三方 API ----------
            listing.Label("<b>第三方 API</b>");
            listing.Label("<color=#9A9A9A>监控子模组注册的 C# API；超预算会返回 Lua 错误。</color>");
            Settings.enableApiTiming = CheckboxRow(listing, "启用耗时预算/告警（关闭后仅统计）", Settings.enableApiTiming, out boxChanged);
            changed |= boxChanged;
            NumericDoubleRow(listing, "单次调用预算 ms（0–1000）", ref Settings.apiCallBudgetMs,
                ref _apiBudgetBuffer, 0f, 1000f);
            NumericDoubleRow(listing, "慢调用告警 ms（0–1000）", ref Settings.apiCallWarnMs,
                ref _apiWarnBuffer, 0f, 1000f);
            DrawStatsSummary(listing);
            if (listing.ButtonText("重置 API 调用统计"))
            {
                RimLua.Internal.LuaApiStats.Reset();
                Messages.Message("RimLua API 统计已重置", MessageTypeDefOf.NeutralEvent, false);
            }
            listing.GapLine(8f);

            // ---------- 持久化 ----------
            listing.Label("<b>持久化</b>");
            listing.Label("<color=#9A9A9A>每个脚本的 state 表随存档保存；此处限制总大小。</color>");
            NumericIntRow(listing, "state 总上限 KB（16–4096）", ref Settings.maxStateKb,
                ref _stateKbBuffer, 16, 4096);
            listing.Label("当前保存的脚本状态：" + LuaStateStore.Count + " 条");
            listing.GapLine(8f);

            // ---------- 运行状态 ----------
            listing.Label("<b>运行状态</b>");
            listing.Label("运行时：" + (LuaRuntime.Initialized ? "已初始化" : "未初始化") +
                          "  ·  已加载脚本：" + LuaScriptLoader.Scripts.Count +
                          "  ·  第三方 API：" + RimLuaApi.RegisteredApiCount);
            listing.Label("冒烟报告：" + RimLuaApi.SmokeReportPath);
            listing.Label("API dump：" + RimLuaApi.ApiDumpPath);
            listing.Label("<color=#9A9A9A>文档：Docs/EXTENSION_API.md、Docs/TUTORIAL_FOR_SUBMOD_AUTHORS.md</color>");

            listing.End();
            _contentHeight = listing.CurHeight + 16f;
            Widgets.EndScrollView();

            // ---------- 底部操作栏 ----------
            Rect footer = new Rect(inRect.x, inRect.yMax - footerHeight + 6f, inRect.width, 30f);
            float half = (footer.width - 8f) * 0.5f;
            if (Widgets.ButtonText(new Rect(footer.x, footer.y, half, footer.height), "保存设置"))
            {
                WriteSettings();
                _dirty = false;
                Messages.Message("RimLua 设置已保存", MessageTypeDefOf.NeutralEvent, false);
            }
            if (Widgets.ButtonText(new Rect(footer.x + half + 8f, footer.y, half, footer.height), "恢复默认设置"))
            {
                Settings.ResetToDefaults();
                ResetBuffers();
                WriteSettings();
                _dirty = false;
                Messages.Message("RimLua 设置已恢复默认", MessageTypeDefOf.NeutralEvent, false);
            }

            // ---------- 变更检测 / 自动保存 ----------
            if (SettingsHash() != hashBefore)
            {
                _dirty = true;
                _lastChangeAt = Time.realtimeSinceStartup;
            }
            if (_dirty && Time.realtimeSinceStartup - _lastChangeAt > 0.6f)
            {
                WriteSettings();
                _dirty = false;
            }
        }

        // ---------- helpers ----------

        private void EnsureBuffers()
        {
            if (_sliceBuffer == null) ResetBuffers();
        }

        private void ResetBuffers()
        {
            _sliceBuffer = Settings.sliceInstructionBudget.ToString();
            _wallBuffer = Settings.scriptWallClockBudgetMs.ToString();
            _apiBudgetBuffer = Settings.apiCallBudgetMs.ToString("F1");
            _apiWarnBuffer = Settings.apiCallWarnMs.ToString("F1");
            _stateKbBuffer = Settings.maxStateKb.ToString();
        }

        private static bool CheckboxRow(Listing_Standard listing, string label, bool value, out bool changed)
        {
            bool current = value;
            listing.CheckboxLabeled(label, ref current);
            changed = current != value;
            return current;
        }

        private static void NumericIntRow(Listing_Standard listing, string label, ref int value, ref string buffer, int min, int max)
        {
            Rect row = listing.GetRect(28f);
            Widgets.Label(new Rect(row.x, row.y, row.width - 140f, row.height), label);
            Widgets.TextFieldNumeric<int>(new Rect(row.xMax - 120f, row.y, 120f, row.height), ref value, ref buffer, min, max);
        }

        private static void NumericDoubleRow(Listing_Standard listing, string label, ref double value, ref string buffer, float min, float max)
        {
            Rect row = listing.GetRect(28f);
            Widgets.Label(new Rect(row.x, row.y, row.width - 140f, row.height), label);
            Widgets.TextFieldNumeric<double>(new Rect(row.xMax - 120f, row.y, 120f, row.height), ref value, ref buffer, min, max);
        }

        private static void DrawTwoButtons(Listing_Standard listing, string left, Action leftAction, string right, Action rightAction)
        {
            Rect row = listing.GetRect(30f);
            float half = (row.width - 8f) * 0.5f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, half, row.height), left)) leftAction();
            if (Widgets.ButtonText(new Rect(row.x + half + 8f, row.y, half, row.height), right)) rightAction();
        }

        private static void DrawStatsSummary(Listing_Standard listing)
        {
            var stats = RimLuaApi.GetApiStats();
            int calls = 0;
            int violations = 0;
            foreach (var stat in stats)
            {
                calls += stat.Calls;
                violations += stat.BudgetViolations;
            }
            listing.Label("统计：" + stats.Count + " 个 API 被调用  ·  总调用 " + calls +
                          "  ·  预算违规 " + violations);
        }

        private static void GenerateApiDump()
        {
            try
            {
                string path = RimLuaApi.WriteApiDump();
                Messages.Message("API dump 已生成：" + path, MessageTypeDefOf.NeutralEvent, false);
            }
            catch (Exception e)
            {
                Log.Error("[RimLua] API dump failed: " + e);
                Messages.Message("API dump 生成失败，详见 Player.log", MessageTypeDefOf.NegativeEvent, false);
            }
        }

        private static void OpenConfigFolder()
        {
            string path = GenFilePaths.ConfigFolderPath;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception e)
            {
                Log.Warning("[RimLua] cannot open config folder: " + e.Message);
            }
        }

        private int SettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Settings.enableLocalScripts.GetHashCode();
                hash = hash * 31 + Settings.autoReloadLocalScripts.GetHashCode();
                hash = hash * 31 + Settings.runSmokeTestsOnLoad.GetHashCode();
                hash = hash * 31 + Settings.enableApiTiming.GetHashCode();
                hash = hash * 31 + Settings.sliceInstructionBudget;
                hash = hash * 31 + Settings.scriptWallClockBudgetMs;
                hash = hash * 31 + Settings.apiCallBudgetMs.GetHashCode();
                hash = hash * 31 + Settings.apiCallWarnMs.GetHashCode();
                hash = hash * 31 + Settings.maxStateKb;
                return hash;
            }
        }
    }
}