using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonSharp.Interpreter;
using Verse;

namespace RimLua
{
    /// <summary>已加载脚本的元信息（用于控制台列表、C#→Lua 调用、诊断）。</summary>
    public sealed class LuaScriptInfo
    {
        public string Id;
        public string FilePath;
        public string Source;
        public string Hash;
        public DateTime LoadedAt;
        public LuaRunResult LastRun;
        public string LastError;

        internal Script Script;
    }

    /// <summary>
    /// 实时脚本加载器 + 脚本仓库：
    ///  - 启动时执行 Mod 自带 Lua/core 与 Config/RimLua/scripts；
    ///  - FileSystemWatcher 监听本地脚本，250 ms 去抖后主线程重载；
    ///  - 每个脚本保留 Script 实例，供 C#→Lua 调用；
    ///  - 执行前后从 LuaStateStore 注入/回收 state 表（持久化）。
    /// </summary>
    public static class LuaScriptLoader
    {
        public const string SelfTestFileName = "_rimlua_selftest.lua";
        private const string LogTag = RimLuaInfo.LogPrefix + "[loader]";

        private static readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
        private static readonly Dictionary<string, DateTime> _debounce = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, LuaScriptInfo> _scripts = new Dictionary<string, LuaScriptInfo>(StringComparer.Ordinal);
        private static readonly object _sync = new object();

        private static FileSystemWatcher _watcher;
        private static string _localDir;
        private static string _coreDir;
        private static bool _started;

        public static string LocalScriptDir { get { return _localDir; } }
        public static string CoreScriptDir { get { return _coreDir; } }
        public static int LoadCount { get; private set; }
        public static int ReloadCount { get; private set; }
        public static string LastScriptPath { get; private set; }
        public static string LastError { get; private set; }
        public static bool SelfTestSeen { get; private set; }
        public static string SelfTestDetail { get; private set; }

        public static List<LuaScriptInfo> Scripts
        {
            get
            {
                lock (_sync)
                {
                    return _scripts.Values.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
                }
            }
        }

        public static void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                _coreDir = Path.Combine(RimLuaMod.ModRootDir ?? ".", "1.7", "Lua", "core");
                if (Directory.Exists(_coreDir))
                {
                    LoadDirectory(_coreDir, "core");
                }

                if (RimLuaMod.Settings == null || RimLuaMod.Settings.enableLocalScripts)
                {
                    _localDir = Path.Combine(GenFilePaths.ConfigFolderPath, "RimLua", "scripts");
                    Directory.CreateDirectory(_localDir);
                    EnsureSampleScript();
                    LoadDirectory(_localDir, "local");

                    if (RimLuaMod.Settings == null || RimLuaMod.Settings.autoReloadLocalScripts)
                    {
                        StartWatcher();
                    }
                }

                Log.Message(LogTag + " started, localDir=" + _localDir + ", loaded=" + LoadCount + ", watcher=" + (_watcher != null));
            }
            catch (Exception e)
            {
                LastError = e.ToString();
                Log.Error(LogTag + " start failed: " + e);
            }
        }

        /// <summary>每帧调用（主线程）：处理文件变化队列。</summary>
        public static void Pump()
        {
            if (!_started) return;

            string changed;
            while (_pending.TryDequeue(out changed))
            {
                lock (_sync) { _debounce[changed] = DateTime.UtcNow; }
            }

            List<string> ready = null;
            lock (_sync)
            {
                DateTime now = DateTime.UtcNow;
                foreach (var pair in _debounce.ToList())
                {
                    if ((now - pair.Value).TotalMilliseconds >= 250)
                    {
                        if (ready == null) ready = new List<string>();
                        ready.Add(pair.Key);
                        _debounce.Remove(pair.Key);
                    }
                }
            }

            if (ready == null) return;
            foreach (string path in ready)
            {
                if (File.Exists(path))
                {
                    ExecuteFile(path, "local", true);
                }
                else
                {
                    RemoveScriptPath(path);
                }
            }
        }

        /// <summary>立即加载/重载一个文件（主线程；供控制台与冒烟测试调用）。</summary>
        public static LuaRunResult LoadFileNow(string path, string source, bool reload)
        {
            return ExecuteFile(path, source, reload);
        }

        public static bool TryGetScript(string id, out LuaScriptInfo info)
        {
            lock (_sync) { return _scripts.TryGetValue(id, out info); }
        }

        public static bool ReloadScript(string id, out string error)
        {
            LuaScriptInfo info;
            lock (_sync) { _scripts.TryGetValue(id, out info); }
            if (info == null)
            {
                error = "script not found: " + id;
                return false;
            }
            if (!File.Exists(info.FilePath))
            {
                error = "file missing: " + info.FilePath;
                return false;
            }
            LuaRunResult result = ExecuteFile(info.FilePath, info.Source, true);
            error = result.IsSuccess ? null : result.Error;
            return result.IsSuccess;
        }

        public static int ReloadAll()
        {
            int ok = 0;
            foreach (LuaScriptInfo info in Scripts)
            {
                string error;
                if (ReloadScript(info.Id, out error)) ok++;
            }
            return ok;
        }

        public static string WriteSelfTestScript()
        {
            if (string.IsNullOrEmpty(_localDir)) return null;
            string path = Path.Combine(_localDir, SelfTestFileName);
            string code =
                "-- RimLua live-reload self test\n" +
                "log.info(\"live-reload self test executed, rim.version=\" .. rim.version)\n" +
                "state.ticks = (state.ticks or 0) + 1\n";
            File.WriteAllText(path, code);
            Log.Message(LogTag + " self-test script written: " + path);
            return path;
        }

        public static string WriteCallTestScript(string fileName, string code)
        {
            if (string.IsNullOrEmpty(_localDir)) return null;
            string path = Path.Combine(_localDir, fileName);
            File.WriteAllText(path, code);
            return path;
        }

        private static void EnsureSampleScript()
        {
            if (string.IsNullOrEmpty(_localDir)) return;
            if (Directory.GetFiles(_localDir, "*.lua").Length > 0) return;
            string sample = Path.Combine(_localDir, "00_hello.lua");
            File.WriteAllText(sample,
                "-- 修改并保存本文件即可触发热加载（RimLua）\n" +
                "log.info(\"hello from RimLua live script, version=\" .. rim.version .. \", tick=\" .. rim.ticks())\n");
            Log.Message(LogTag + " sample script created: " + sample);
        }

        private static void LoadDirectory(string dir, string source)
        {
            string[] files = Directory.GetFiles(dir, "*.lua", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                ExecuteFile(file, source, false);
            }
        }

        private static LuaRunResult ExecuteFile(string path, string source, bool reload)
        {
            string fileName = Path.GetFileName(path);
            string id = source + "/" + fileName;
            var result = new LuaRunResult { Status = LuaRunStatus.Error, Error = "not executed" };
            try
            {
                string code = File.ReadAllText(path);
                string hash = Fnv1a(code);
                Script script = LuaRuntime.CreateScript();
                LuaRuntime.InstallGlobals(script, id);

                int budgetMs = RimLuaMod.Settings != null ? RimLuaMod.Settings.scriptWallClockBudgetMs : 50;
                int slice = RimLuaMod.Settings != null ? RimLuaMod.Settings.sliceInstructionBudget : 5000;
                result = LuaRuntime.RunChunk(script, code, fileName, budgetMs, slice);

                if (result.IsSuccess)
                {
                    LuaStateStore.Capture(id, script);
                }

                lock (_sync)
                {
                    LuaScriptInfo info;
                    if (!_scripts.TryGetValue(id, out info)) info = new LuaScriptInfo { Id = id };
                    info.FilePath = path;
                    info.Source = source;
                    info.Hash = hash;
                    info.LastRun = result;
                    info.LastError = result.IsSuccess ? null : result.Error;
                    info.LoadedAt = DateTime.Now;
                    if (result.IsSuccess) info.Script = script;
                    _scripts[id] = info;
                }

                LoadCount++;
                if (reload) ReloadCount++;
                LastScriptPath = path;

                bool isSelfTest = string.Equals(fileName, SelfTestFileName, StringComparison.OrdinalIgnoreCase);
                if (isSelfTest)
                {
                    SelfTestSeen = true;
                    SelfTestDetail = result.ToString();
                }

                if (result.IsSuccess)
                {
                    Log.Message(LogTag + " executed " + id + " (" + result + ")");
                }
                else
                {
                    LastError = result.Error;
                    Log.Warning(LogTag + " script failed " + id + ": " + result);
                }
            }
            catch (Exception e)
            {
                result.Status = LuaRunStatus.Error;
                result.Error = e.GetType().Name + ": " + e.Message;
                LastError = e.ToString();
                Log.Error(LogTag + " execute failed " + path + ": " + e);
            }
            return result;
        }

        private static void RemoveScriptPath(string path)
        {
            string fileName = Path.GetFileName(path);
            lock (_sync)
            {
                var keys = new List<string>();
                foreach (var pair in _scripts)
                {
                    if (string.Equals(pair.Value.FilePath, path, StringComparison.OrdinalIgnoreCase)) keys.Add(pair.Key);
                }
                foreach (string key in keys) _scripts.Remove(key);
            }
            Log.Message(LogTag + " script removed: " + fileName);
        }

        private static void StartWatcher()
        {
            _watcher = new FileSystemWatcher(_localDir, "*.lua");
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size;
            _watcher.IncludeSubdirectories = false;
            _watcher.EnableRaisingEvents = true;
            _watcher.Created += (s, e) => Enqueue(e.FullPath);
            _watcher.Changed += (s, e) => Enqueue(e.FullPath);
            _watcher.Deleted += (s, e) => Enqueue(e.FullPath);
            _watcher.Renamed += (s, e) => Enqueue(e.FullPath);
            _watcher.Error += (s, e) => Log.Error(LogTag + " watcher error: " + e.GetException());
        }

        private static void Enqueue(string path)
        {
            if (!string.IsNullOrEmpty(path)) _pending.Enqueue(path);
        }

        private static string Fnv1a(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }
    }
}
