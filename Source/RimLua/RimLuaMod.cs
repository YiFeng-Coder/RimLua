using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLua
{
    /// <summary>Mod 入口 + 设置页。</summary>
    public class RimLuaMod : Mod
    {
        public static RimLuaMod Instance { get; private set; }
        public static RimLuaSettings Settings { get; private set; }

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
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            bool changed = false;

            bool enable = Settings.enableLocalScripts;
            listing.CheckboxLabeled("启用本地脚本（Config/RimLua/scripts）", ref enable);
            if (enable != Settings.enableLocalScripts) { Settings.enableLocalScripts = enable; changed = true; }

            bool autoReload = Settings.autoReloadLocalScripts;
            listing.CheckboxLabeled("自动热加载本地脚本", ref autoReload);
            if (autoReload != Settings.autoReloadLocalScripts) { Settings.autoReloadLocalScripts = autoReload; changed = true; }

            bool smoke = Settings.runSmokeTestsOnLoad;
            listing.CheckboxLabeled("启动时运行冒烟测试（开发用）", ref smoke);
            if (smoke != Settings.runSmokeTestsOnLoad) { Settings.runSmokeTestsOnLoad = smoke; changed = true; }

            bool timing = Settings.enableApiTiming;
            listing.CheckboxLabeled("启用 API 耗时预算/告警（关闭后仅统计）", ref timing);
            if (timing != Settings.enableApiTiming) { Settings.enableApiTiming = timing; changed = true; }

            listing.Gap(6f);

            double budget = Settings.apiCallBudgetMs;
            string budgetText = listing.TextEntryLabeled("API 单次调用预算 (ms)", budget.ToString("F1"), 8);
            double parsedBudget;
            if (double.TryParse(budgetText, out parsedBudget) && Math.Abs(parsedBudget - budget) > 0.0001)
            {
                Settings.apiCallBudgetMs = Math.Max(0.0, parsedBudget);
                changed = true;
            }

            double warn = Settings.apiCallWarnMs;
            string warnText = listing.TextEntryLabeled("API 慢调用告警阈值 (ms)", warn.ToString("F1"), 8);
            double parsedWarn;
            if (double.TryParse(warnText, out parsedWarn) && Math.Abs(parsedWarn - warn) > 0.0001)
            {
                Settings.apiCallWarnMs = Math.Max(0.0, parsedWarn);
                changed = true;
            }

            int slice = Settings.sliceInstructionBudget;
            string sliceText = listing.TextEntryLabeled("每片指令数", slice.ToString(), 8);
            int parsedSlice;
            if (int.TryParse(sliceText, out parsedSlice) && parsedSlice != slice)
            {
                Settings.sliceInstructionBudget = Math.Max(100, parsedSlice);
                changed = true;
            }

            int wall = Settings.scriptWallClockBudgetMs;
            string wallText = listing.TextEntryLabeled("单次脚本墙钟预算 (ms)", wall.ToString(), 8);
            int parsedWall;
            if (int.TryParse(wallText, out parsedWall) && parsedWall != wall)
            {
                Settings.scriptWallClockBudgetMs = Math.Max(1, parsedWall);
                changed = true;
            }

            listing.GapLine(12f);
            if (listing.ButtonText("打开 Lua 控制台"))
            {
                Find.WindowStack.Add(new Window_LuaConsole());
            }
            if (listing.ButtonText("保存设置"))
            {
                Settings.Write();
                changed = false;
                Messages.Message("RimLua 设置已保存", MessageTypeDefOf.NeutralEvent, false);
            }
            if (listing.ButtonText("重置 API 调用统计"))
            {
                RimLua.Internal.LuaApiStats.Reset();
                Messages.Message("RimLua API 统计已重置", MessageTypeDefOf.NeutralEvent, false);
            }

            listing.Label("已加载脚本: " + LuaScriptLoader.Scripts.Count + "，第三方 API: " + RimLuaApi.RegisteredApiCount);
            if (changed) Settings.Write();
            listing.End();
        }
    }
}