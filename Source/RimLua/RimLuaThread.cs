using System.Threading;

namespace RimLua
{
    /// <summary>
    /// 主线程探针。注意：RimWorld 的 Game/FinalizeInit 在快速测试与部分加载流程中
    /// 可能运行在后台线程（LongEventHandler.RunEventFromAnotherThread），因此不能在
    /// 静态构造或 FinalizeInit 里捕获主线程 ID；必须在第一次 GameComponentTick 捕获。
    /// </summary>
    public static class RimLuaThread
    {
        private static int _mainThreadId = -1;

        public static void CaptureIfNeeded()
        {
            if (_mainThreadId < 0)
            {
                _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            }
        }

        public static int MainThreadId { get { return _mainThreadId; } }

        public static bool IsMainThread
        {
            get { return _mainThreadId >= 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId; }
        }
    }
}
