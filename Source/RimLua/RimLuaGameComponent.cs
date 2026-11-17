using Verse;

namespace RimLua
{
    /// <summary>
    /// 运行时宿主：
    ///  - 首次 GameComponentUpdate（主线程）初始化 Lua、加载脚本、跑冒烟；
    ///  - 每帧泵实时脚本加载器、控制台队列、冒烟异步断言；
    ///  - 存档/读档保存脚本 state（JSON）。
    /// </summary>
    public class RimLuaGameComponent : GameComponent
    {
        private bool _started;

        public RimLuaGameComponent(Game game)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // 不在这里初始化：FinalizeInit 可能位于后台加载线程。
        }

        public override void GameComponentUpdate()
        {
            if (!_started)
            {
                _started = true;
                StartRuntimeOnMainThread();
            }

            LuaScriptLoader.Pump();
            LuaConsoleService.Pump();
            LuaSmokeTests.Pump();
        }

        private static void StartRuntimeOnMainThread()
        {
            RimLuaThread.CaptureIfNeeded();
            LuaRuntime.EnsureInitialized();

            if (!LuaRuntime.Initialized)
            {
                return;
            }

            LuaScriptLoader.Start();
            if (RimLuaMod.Settings == null || RimLuaMod.Settings.runSmokeTestsOnLoad)
            {
                LuaSmokeTests.Begin();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            string states = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                states = LuaStateStore.SerializeForSave();
            }
            Scribe_Values.Look(ref states, "luaStates", null);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && !string.IsNullOrEmpty(states))
            {
                LuaStateStore.LoadFrom(states);
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            LuaStateStore.Clear();
        }
    }
}
