using System;
using System.Collections.Generic;
using System.Text.Json;
using MoonSharp.Interpreter;
using Verse;

namespace RimLua
{
    /// <summary>
    /// 每个脚本的持久状态（Lua 全局 state 表）：
    ///  - 只允许纯数据：nil/boolean/number/string/数组/字符串键表；
    ///  - 深度、节点数、字符串长度、总大小都有上限；
    ///  - 存档时序列化为 JSON 字符串，由 RimLuaGameComponent.ExposeData 保存。
    /// </summary>
    public static class LuaStateStore
    {
        private const int MaxDepth = 16;
        private const int MaxNodes = 4096;
        private const int MaxStringLength = 8192;
        private const int DefaultMaxTotalJsonBytes = 256 * 1024;
        private const string LogTag = RimLuaInfo.LogPrefix + "[state]";

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, object> _states = new Dictionary<string, object>(StringComparer.Ordinal);

        public static int Count
        {
            get { lock (_lock) { return _states.Count; } }
        }

        public static void Clear()
        {
            lock (_lock) { _states.Clear(); }
        }

        /// <summary>把存档状态注入脚本的全局 state 表（没有则创建空表）。</summary>
        public static void Apply(string scriptId, Script script)
        {
            Table table = new Table(script);
            object tree;
            bool exists;
            lock (_lock) { exists = _states.TryGetValue(scriptId, out tree); }
            if (exists)
            {
                int nodes = 0;
                Populate(script, table, tree, 0, ref nodes);
            }
            script.Globals["state"] = DynValue.NewTable(table);
        }

        /// <summary>把脚本执行后的 state 表转回 C# 树并保存。</summary>
        public static void Capture(string scriptId, Script script)
        {
            DynValue value = script.Globals.Get("state");
            if (value == null || value.Type != DataType.Table)
            {
                return;
            }

            int nodes = 0;
            object tree = ConvertTable(value.Table, 0, ref nodes, new HashSet<Table>());
            lock (_lock) { _states[scriptId] = tree; }
        }

        public static string SerializeForSave()
        {
            Dictionary<string, object> copy;
            lock (_lock)
            {
                copy = new Dictionary<string, object>(_states, StringComparer.Ordinal);
            }
            try
            {
                int configuredKb = RimLuaMod.Settings != null ? RimLuaMod.Settings.maxStateKb : 256;
                int maxBytes = Math.Max(16, configuredKb) * 1024;
                string json = JsonSerializer.Serialize(copy);
                if (json.Length > maxBytes)
                {
                    Log.Error(LogTag + " state json too large (" + json.Length + " bytes > " + maxBytes + "), saved empty state");
                    return "{}";
                }
                return json;
            }
            catch (Exception e)
            {
                Log.Error(LogTag + " serialize failed: " + e);
                return "{}";
            }
        }

        public static void LoadFrom(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                Dictionary<string, JsonElement> raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                var loaded = new Dictionary<string, object>(StringComparer.Ordinal);
                if (raw != null)
                {
                    foreach (var pair in raw)
                    {
                        loaded[pair.Key] = FromJson(pair.Value, 0);
                    }
                }
                lock (_lock)
                {
                    _states.Clear();
                    foreach (var pair in loaded) _states[pair.Key] = pair.Value;
                }
                Log.Message(LogTag + " loaded state for " + loaded.Count + " script(s)");
            }
            catch (Exception e)
            {
                Log.Error(LogTag + " load failed: " + e);
            }
        }

        public static bool TryGetStateJson(string scriptId, string indent, out string json)
        {
            object tree;
            lock (_lock)
            {
                if (!_states.TryGetValue(scriptId, out tree))
                {
                    json = null;
                    return false;
                }
            }
            try
            {
                json = JsonSerializer.Serialize(tree, new JsonSerializerOptions { WriteIndented = string.IsNullOrEmpty(indent) == false });
            }
            catch (Exception e)
            {
                json = "serialize error: " + e.Message;
            }
            return true;
        }

        // ---------- Lua table <-> C# tree ----------

        private static object ConvertTable(Table table, int depth, ref int nodes, HashSet<Table> seen)
        {
            if (depth > MaxDepth) return null;
            if (!seen.Add(table)) return null;          // 环引用：丢弃
            nodes++;
            if (nodes > MaxNodes) return null;

            if (IsArray(table))
            {
                var list = new List<object>();
                int length = table.Length;
                for (int i = 1; i <= length; i++)
                {
                    if (nodes > MaxNodes) break;
                    list.Add(ConvertValue(table.Get(i), depth + 1, ref nodes, seen));
                }
                return list;
            }

            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (TablePair pair in table.Pairs)
            {
                if (nodes > MaxNodes) break;
                string key = KeyToString(pair.Key);
                if (key == null) continue;
                dict[key] = ConvertValue(pair.Value, depth + 1, ref nodes, seen);
            }
            return dict;
        }

        private static object ConvertValue(DynValue value, int depth, ref int nodes, HashSet<Table> seen)
        {
            if (value == null) return null;
            switch (value.Type)
            {
                case DataType.Nil:
                case DataType.Void:
                    return null;
                case DataType.Boolean:
                    return value.Boolean;
                case DataType.Number:
                    return value.Number;
                case DataType.String:
                    string text = value.String ?? string.Empty;
                    return text.Length > MaxStringLength ? text.Substring(0, MaxStringLength) : text;
                case DataType.Table:
                    return ConvertTable(value.Table, depth, ref nodes, seen);
                default:
                    return null;                    // 函数/userdata/协程不入档
            }
        }

        private static bool IsArray(Table table)
        {
            if (table.Length <= 0) return false;
            foreach (TablePair pair in table.Pairs)
            {
                if (pair.Key.Type != DataType.Number) return false;
                double number = pair.Key.Number;
                if (Math.Abs(number - Math.Round(number)) > 0.000001) return false;
            }
            return true;
        }

        private static string KeyToString(DynValue key)
        {
            if (key == null) return null;
            switch (key.Type)
            {
                case DataType.String: return key.String;
                case DataType.Number: return key.Number.ToString("R");
                case DataType.Boolean: return key.Boolean ? "true" : "false";
                default: return null;
            }
        }

        private static void Populate(Script script, Table table, object tree, int depth, ref int nodes)
        {
            if (tree == null || depth > MaxDepth) return;
            nodes++;
            if (nodes > MaxNodes) return;

            if (tree is Dictionary<string, object> dict)
            {
                foreach (var pair in dict)
                {
                    table.Set(pair.Key, ToLuaValue(script, pair.Value, depth + 1, ref nodes));
                }
                return;
            }
            if (tree is List<object> list)
            {
                foreach (object item in list)
                {
                    table.Append(ToLuaValue(script, item, depth + 1, ref nodes));
                }
            }
        }

        private static DynValue ToLuaValue(Script script, object value, int depth, ref int nodes)
        {
            if (value == null) return DynValue.Nil;
            if (value is bool b) return DynValue.NewBoolean(b);
            if (value is double d) return DynValue.NewNumber(d);
            if (value is string s) return DynValue.NewString(s);
            if (value is Dictionary<string, object> || value is List<object>)
            {
                var table = new Table(script);
                Populate(script, table, value, depth, ref nodes);
                return DynValue.NewTable(table);
            }
            return DynValue.Nil;
        }

        private static object FromJson(JsonElement element, int depth)
        {
            if (depth > MaxDepth) return null;
            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number: return element.GetDouble();
                case JsonValueKind.String:
                    string s = element.GetString() ?? string.Empty;
                    return s.Length > MaxStringLength ? s.Substring(0, MaxStringLength) : s;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        list.Add(FromJson(item, depth + 1));
                    }
                    return list;
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        dict[property.Name] = FromJson(property.Value, depth + 1);
                    }
                    return dict;
                default:
                    return null;
            }
        }
    }
}
