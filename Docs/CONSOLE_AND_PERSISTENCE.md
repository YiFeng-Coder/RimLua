# RimLua 控制台与脚本持久化

- 版本：RimLua 0.1.0-m0，ApiVersion 1
- 适用：RimWorld 1.7
- 范围：控制台（REPL）+ 脚本 state 持久化 + C#→Lua 调用说明见 [CALL_LUA_AND_APIDUMP.md](CALL_LUA_AND_APIDUMP.md)

---

## 1. 打开控制台

游戏内：**选项 → 模组设置 → RimLua → “打开 Lua 控制台”**。

窗口支持：

- 单行输入 + Run 按钮；
- 按钮：Reload all / List / API / API dump / Stats / Clear；
- 输出区域可滚动，保留最近 300 行；
- 所有执行排队到**下一帧主线程**（不在 OnGUI 里直接跑 Lua）。

## 2. 输入规则

| 输入 | 行为 |
| --- | --- |
| `= 1 + 2` | 表达式，自动补 `return`，显示结果 |
| `log.info("hi")` | 普通 Lua 代码块 |
| `:help` | 命令帮助 |
| `:list` | 已加载脚本（id / 状态 / hash） |
| `:reload` | 重载全部脚本 |
| `:reload <id>` | 重载单个脚本，如 `:reload local/foo.lua` |
| `:api` | 在控制台打印 API dump |
| `:apidump` | 写 `<Config>/RimLua/api-dump.md` |
| `:stats` | 第三方 API 调用次数/失败/预算违规/耗时 |
| `:smoke` | 重新运行冒烟测试（主线程） |
| `:state <id>` | 查看某脚本持久 state 的 JSON |
| `:clear` | 清空控制台输出 |

注意：控制台执行的代码是“匿名脚本”，**不**持久化 state；需要持久化的逻辑请放 `Config/RimLua/scripts/*.lua`。

## 3. 脚本持久化（state）

### 3.1 用法

每个脚本自动获得一个全局 `state` 表：

```lua
-- Config/RimLua/scripts/counter.lua
state.count = (state.count or 0) + 1
log.info("本脚本已执行 " .. state.count .. " 次")
```

- 脚本首次加载、热重载、C# 调用后，state 都会被同步回宿主；
- 每次执行前，宿主把上次保存的 state 注入为 `state` 全局；
- 存读档时 state 随存档保存/恢复；
- 开新游戏时清空。

### 3.2 支持的数据

| Lua | 存储 |
| --- | --- |
| nil / boolean / number / string | 直接保存 |
| 字符串键 table | JSON object |
| 数组 table（整数键 1..n） | JSON array |
| 函数 / userdata / 协程 / 环引用 | **丢弃**（不报错，记录 debug） |

限制（防止存档膨胀）：

| 限制 | 值 |
| --- | --- |
| 最大嵌套深度 | 16 |
| 最大节点数 | 4096 |
| 单字符串最大长度 | 8192 |
| 所有脚本 state 序列化后总大小 | 256 KiB（超出则保存空状态并报错） |

### 3.3 热重载语义

- 修改脚本文件 → 重新执行该脚本 → **沿用**上次的 state；
- 需要“重载时清空状态”可在脚本里判断版本号自行重置，例如：
  ```lua
  if state.version ~= "2" then state = { version = "2" } end
  ```
- 文件名不合法/语法错误时，旧脚本实例与旧 state 保留，日志记录失败原因。

### 3.4 存档格式

`RimLuaGameComponent.ExposeData` 中保存一个字段 `luaStates`（JSON 字符串），结构示例：

```json
{
  "local/counter.lua": { "count": 3 },
  "core/init.lua": {}
}
```

> 说明：state 只保存“纯数据”。如果你需要跨存档保存复杂对象，请自己转成字符串/表。

## 4. 限制

- state 不保存函数、闭包、userdata、协程；
- 混合键表会退化为字符串键 object；
- 脚本执行失败时不回写 state（避免半更新）；
- 只有脚本级 state，没有全局共享 KV（后续需要再加）。

## 5. 验证

- 独立 harness：state 计数 1→2、JSON 存读档还原、控制台表达式执行均通过。
- 游戏内：见 [M2-PLATFORM-REPORT.md](M2-PLATFORM-REPORT.md)（冒烟测试含 state/console/dump 用例）。
