using System;
using System.Collections.Generic;
using RimLua.Api;

namespace RimLua.Internal
{
    /// <summary>API 调用耗时统计与预算违规计数（主线程使用）。</summary>
    internal static class LuaApiStats
    {
        private sealed class Entry
        {
            public string Path;
            public int Calls;
            public int Failures;
            public int BudgetViolations;
            public int Warnings;
            public double TotalMs;
            public double MaxMs;
            public double LastMs;
        }

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Entry> _records = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public static void Record(string path, double elapsedMs, bool ok)
        {
            lock (_lock)
            {
                Entry record = Get(path);
                record.Calls++;
                record.LastMs = elapsedMs;
                record.TotalMs += elapsedMs;
                if (elapsedMs > record.MaxMs) record.MaxMs = elapsedMs;
                if (!ok) record.Failures++;
            }
        }

        public static void RecordBudgetViolation(string path)
        {
            lock (_lock) { Get(path).BudgetViolations++; }
        }

        /// <summary>返回 true 表示应该输出警告（每个 API 最多 3 次）。</summary>
        public static bool ShouldWarn(string path)
        {
            lock (_lock)
            {
                Entry record = Get(path);
                record.Warnings++;
                return record.Warnings <= 3;
            }
        }

        public static List<LuaApiStat> Snapshot()
        {
            var list = new List<LuaApiStat>();
            lock (_lock)
            {
                foreach (var pair in _records)
                {
                    list.Add(new LuaApiStat
                    {
                        Path = pair.Value.Path,
                        Calls = pair.Value.Calls,
                        Failures = pair.Value.Failures,
                        BudgetViolations = pair.Value.BudgetViolations,
                        TotalMs = pair.Value.TotalMs,
                        MaxMs = pair.Value.MaxMs,
                        LastMs = pair.Value.LastMs
                    });
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return list;
        }

        public static void Reset()
        {
            lock (_lock) { _records.Clear(); }
        }

        private static Entry Get(string path)
        {
            Entry record;
            if (!_records.TryGetValue(path, out record))
            {
                record = new Entry { Path = path };
                _records[path] = record;
            }
            return record;
        }
    }
}