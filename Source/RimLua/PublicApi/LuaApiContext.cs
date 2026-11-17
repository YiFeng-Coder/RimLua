using Verse;

namespace RimLua.Api
{
    /// <summary>
    /// 传给 Lua API 的上下文。API 方法可以把 LuaApiContext 放在任意参数位置，
    /// 宿主会自动注入（不消耗 Lua 参数）。
    /// </summary>
    public sealed class LuaApiContext
    {
        public string ApiName { get; private set; }
        public long GameTick { get; private set; }
        public bool IsMainThread { get; private set; }

        internal LuaApiContext(string apiName)
        {
            ApiName = apiName;
            IsMainThread = RimLuaThread.IsMainThread;
            try
            {
                GameTick = Current.Game != null && Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            }
            catch
            {
                GameTick = 0;
            }
        }
    }
}
