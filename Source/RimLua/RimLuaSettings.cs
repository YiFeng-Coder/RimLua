using Verse;

namespace RimLua
{
    /// <summary>RimLua 设置（默认值即安全值；UI 在 Mod 设置页）。</summary>
    public class RimLuaSettings : ModSettings
    {
        public bool enableLocalScripts = true;
        public bool autoReloadLocalScripts = true;
        public bool runSmokeTestsOnLoad = true;
        public int sliceInstructionBudget = 5000;
        public int scriptWallClockBudgetMs = 50;

        // API 调用耗时监控
        public bool enableApiTiming = true;
        public double apiCallBudgetMs = 10.0;
        public double apiCallWarnMs = 2.0;

        // 持久化
        public int maxStateKb = 256;

        public void ResetToDefaults()
        {
            enableLocalScripts = true;
            autoReloadLocalScripts = true;
            runSmokeTestsOnLoad = true;
            sliceInstructionBudget = 5000;
            scriptWallClockBudgetMs = 50;
            enableApiTiming = true;
            apiCallBudgetMs = 10.0;
            apiCallWarnMs = 2.0;
            maxStateKb = 256;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableLocalScripts, "enableLocalScripts", true);
            Scribe_Values.Look(ref autoReloadLocalScripts, "autoReloadLocalScripts", true);
            Scribe_Values.Look(ref runSmokeTestsOnLoad, "runSmokeTestsOnLoad", true);
            Scribe_Values.Look(ref sliceInstructionBudget, "sliceInstructionBudget", 5000);
            Scribe_Values.Look(ref scriptWallClockBudgetMs, "scriptWallClockBudgetMs", 50);
            Scribe_Values.Look(ref enableApiTiming, "enableApiTiming", true);
            Scribe_Values.Look(ref apiCallBudgetMs, "apiCallBudgetMs", 10.0);
            Scribe_Values.Look(ref apiCallWarnMs, "apiCallWarnMs", 2.0);
            Scribe_Values.Look(ref maxStateKb, "maxStateKb", 256);
        }
    }
}
