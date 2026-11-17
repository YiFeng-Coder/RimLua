using UnityEngine;
using Verse;

namespace RimLua
{
    /// <summary>RimLua 控制台窗口（不含执行逻辑；执行由 LuaConsoleService 在主线程泵）。</summary>
    public class Window_LuaConsole : Window
    {
        private string _input = string.Empty;
        private Vector2 _scroll;

        public Window_LuaConsole()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            closeOnAccept = false;
            onlyOneOfTypeAllowed = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(960f, 620f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f),
                "RimLua 控制台 — 输入 Lua 代码，或 :help 查看命令。执行发生在下一帧主线程。");

            const float buttonWidth = 86f;
            Rect inputRect = new Rect(0f, 28f, inRect.width - buttonWidth - 8f, 30f);
            _input = Widgets.TextField(inputRect, _input);
            if (Widgets.ButtonText(new Rect(inRect.width - buttonWidth, 28f, buttonWidth, 30f), "Run"))
            {
                LuaConsoleService.Submit(_input);
                _input = string.Empty;
            }

            float y = 64f;
            float x = 0f;
            if (Widgets.ButtonText(new Rect(x, y, 86f, 26f), "Reload all")) { LuaConsoleService.Submit(":reload"); }
            x += 92f;
            if (Widgets.ButtonText(new Rect(x, y, 70f, 26f), "List")) { LuaConsoleService.Submit(":list"); }
            x += 76f;
            if (Widgets.ButtonText(new Rect(x, y, 70f, 26f), "API")) { LuaConsoleService.Submit(":api"); }
            x += 76f;
            if (Widgets.ButtonText(new Rect(x, y, 86f, 26f), "API dump")) { LuaConsoleService.Submit(":apidump"); }
            x += 92f;
            if (Widgets.ButtonText(new Rect(x, y, 76f, 26f), "Stats")) { LuaConsoleService.Submit(":stats"); }
            x += 82f;
            if (Widgets.ButtonText(new Rect(x, y, 76f, 26f), "Clear"))
            {
                LuaConsoleService.Clear();
            }

            Rect outRect = new Rect(0f, y + 32f, inRect.width, inRect.height - (y + 32f));
            var lines = LuaConsoleService.GetLines();
            float lineHeight = 22f;
            float contentHeight = Mathf.Max(outRect.height, lines.Count * lineHeight + 4f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, contentHeight);

            Widgets.BeginScrollView(outRect, ref _scroll, viewRect, true);
            if (_scroll.y > 0f && lines.Count > 0)
            {
                // 顶部内容占位，保持滚动范围稳定
            }
            for (int i = 0; i < lines.Count; i++)
            {
                Widgets.Label(new Rect(0f, i * lineHeight, viewRect.width, lineHeight), lines[i]);
            }
            Widgets.EndScrollView();
        }

        public override void PostClose()
        {
            base.PostClose();
            _input = string.Empty;
        }
    }
}
