using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MoonSharp.Interpreter;
using RimLua.Api;
using Verse;

namespace RimLua.Internal
{
    internal abstract class LuaApiEntry
    {
        public string Path;
        public string Kind;
        public string Signature;

        public abstract DynValue Build(Script script);

        internal virtual DynValue Invoke(Script script, CallbackArguments args)
        {
            throw new ScriptRuntimeException("[" + Path + "] is not callable");
        }
    }

    internal sealed class LuaApiValueEntry : LuaApiEntry
    {
        public object Value;

        public LuaApiValueEntry(string path, object value)
        {
            Path = path;
            Value = value;
            Kind = "value";
            Signature = (value != null ? value.GetType().Name : "null") + " " + path;
        }

        public override DynValue Build(Script script)
        {
            return LuaValueConverter.FromClr(script, Value, Path);
        }
    }

    internal sealed class LuaApiCallableEntry : LuaApiEntry
    {
        private readonly ParameterInfo[] _parameters;
        private readonly Type _returnType;
        private readonly Func<object[], object> _invoke;
        private int _failures;

        public LuaApiCallableEntry(string path, ParameterInfo[] parameters, Type returnType, Func<object[], object> invoke)
        {
            Path = path;
            _parameters = parameters;
            _returnType = returnType;
            _invoke = invoke;
            Kind = "function";
            Signature = BuildSignature();
        }

        private string BuildSignature()
        {
            var parts = new List<string>();
            foreach (ParameterInfo parameter in _parameters)
            {
                if (parameter.ParameterType == typeof(LuaApiContext)) continue;
                parts.Add(FriendlyType(parameter.ParameterType));
            }
            return FriendlyType(_returnType) + " " + Path + "(" + string.Join(", ", parts) + ")";
        }

        private static string FriendlyType(Type type)
        {
            if (type == null) return "?";
            if (type == typeof(void)) return "void";
            if (type.IsArray) return FriendlyType(type.GetElementType()) + "[]";
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return "List<" + FriendlyType(type.GetGenericArguments()[0]) + ">";
            return type.Name;
        }

        public override DynValue Build(Script script)
        {
            return DynValue.NewCallback((context, args) => Invoke(script, args), Path);
        }

        internal override DynValue Invoke(Script script, CallbackArguments args)
        {
            int contextCount = 0;
            for (int i = 0; i < _parameters.Length; i++)
            {
                if (_parameters[i].ParameterType == typeof(LuaApiContext)) contextCount++;
            }

            int expected = _parameters.Length - contextCount;
            if (args.Count != expected)
            {
                throw new ScriptRuntimeException("[" + Path + "] expects " + expected + " argument(s), got " + args.Count);
            }

            object[] callArgs = new object[_parameters.Length];
            int argIndex = 0;
            for (int i = 0; i < _parameters.Length; i++)
            {
                Type parameterType = _parameters[i].ParameterType;
                if (parameterType == typeof(LuaApiContext))
                {
                    callArgs[i] = new LuaApiContext(Path);
                    continue;
                }

                try
                {
                    callArgs[i] = LuaValueConverter.ToClr(args[argIndex], parameterType, Path);
                }
                catch (LuaApiException e)
                {
                    throw new ScriptRuntimeException(e.Message);
                }
                argIndex++;
            }

            var watch = Stopwatch.StartNew();
            try
            {
                object result = _invoke(callArgs);
                watch.Stop();
                LuaApiStats.Record(Path, watch.Elapsed.TotalMilliseconds, true);
                EnforceBudget(watch.Elapsed.TotalMilliseconds);
                return LuaValueConverter.FromClr(script, result, Path);
            }
            catch (TargetInvocationException e)
            {
                throw MakeError(e.InnerException ?? e, watch);
            }
            catch (LuaApiException e)
            {
                throw MakeError(e, watch);
            }
            catch (ScriptRuntimeException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw MakeError(e, watch);
            }
        }

        private void EnforceBudget(double elapsedMs)
        {
            if (RimLuaMod.Settings != null && !RimLuaMod.Settings.enableApiTiming)
            {
                return;
            }

            double budget = RimLuaMod.Settings != null ? RimLuaMod.Settings.apiCallBudgetMs : 10.0;
            if (budget > 0 && elapsedMs > budget)
            {
                LuaApiStats.RecordBudgetViolation(Path);
                throw new ScriptRuntimeException("[" + Path + "] API exceeded budget (" +
                    elapsedMs.ToString("F1") + " ms > " + budget.ToString("F0") + " ms)");
            }

            double warn = RimLuaMod.Settings != null ? RimLuaMod.Settings.apiCallWarnMs : 2.0;
            if (warn > 0 && elapsedMs > warn && LuaApiStats.ShouldWarn(Path))
            {
                Log.Warning(RimLuaInfo.LogPrefix + "[api] " + Path + " slow: " + elapsedMs.ToString("F1") + " ms");
            }
        }

        private ScriptRuntimeException MakeError(Exception e, Stopwatch watch)
        {
            watch.Stop();
            LuaApiStats.Record(Path, watch.Elapsed.TotalMilliseconds, false);
            _failures++;
            if (_failures <= 3)
            {
                Log.Warning(RimLuaInfo.LogPrefix + "[api] " + Path + " failed: " + e);
            }
            return new ScriptRuntimeException("[" + Path + "] " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// 第三方 Lua API 注册表。线程安全；允许在 RimLua 初始化前后任意时刻注册。
    /// 注册路径相对于 rim 根表，例如 "mymod.greet" => rim.mymod.greet。
    /// </summary>
    internal static class LuaApiRegistry
    {
        private const string LogTag = RimLuaInfo.LogPrefix + "[api]";
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, LuaApiEntry> _entries =
            new Dictionary<string, LuaApiEntry>(StringComparer.Ordinal);
        private static readonly List<Action> _readyCallbacks = new List<Action>();
        private static readonly HashSet<string> _reservedRoots = new HashSet<string>(StringComparer.Ordinal)
        {
            "version", "api_version", "api_count", "add", "ticks", "is_main_thread", "log"
        };

        private static bool _ready;

        public static int Count
        {
            get { lock (_lock) { return _entries.Count; } }
        }

        public static List<LuaApiInfo> GetInfos()
        {
            List<LuaApiEntry> snapshot;
            lock (_lock)
            {
                snapshot = new List<LuaApiEntry>(_entries.Values);
            }
            snapshot.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            var infos = new List<LuaApiInfo>(snapshot.Count);
            foreach (LuaApiEntry entry in snapshot)
            {
                infos.Add(new LuaApiInfo { Path = entry.Path, Kind = entry.Kind, Signature = entry.Signature });
            }
            return infos;
        }

        public static bool RegisterFunction(string path, Delegate function)
        {
            if (function == null)
            {
                Log.Error(LogTag + " RegisterFunction('" + path + "') got null delegate");
                return false;
            }

            MethodInfo method = function.Method;
            if (method.ContainsGenericParameters)
            {
                Log.Error(LogTag + " RegisterFunction('" + path + "') does not support generic delegates");
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (!ValidateSignature(path, parameters, method.ReturnType, out int contextCount))
            {
                return false;
            }

            var entry = new LuaApiCallableEntry(path, parameters, method.ReturnType, args => function.DynamicInvoke(args));
            return TryAdd(path, entry);
        }

        public static bool RegisterGlobal(string path, object value)
        {
            if (value == null)
            {
                Log.Error(LogTag + " RegisterGlobal('" + path + "') got null value");
                return false;
            }

            if (!LuaValueConverter.IsSupportedReturn(value.GetType()))
            {
                Log.Error(LogTag + " RegisterGlobal('" + path + "') unsupported value type " + value.GetType().FullName);
                return false;
            }

            var entry = new LuaApiValueEntry(path, value);
            return TryAdd(path, entry);
        }

        public static int RegisterModule(Type moduleType)
        {
            return RegisterModule(moduleType, null);
        }

        public static int RegisterModule(object moduleInstance)
        {
            if (moduleInstance == null)
            {
                Log.Error(LogTag + " RegisterModule got null instance");
                return 0;
            }
            return RegisterModule(moduleInstance.GetType(), moduleInstance);
        }

        public static int RegisterAllModules(Assembly assembly)
        {
            if (assembly == null) return 0;
            int registered = 0;
            foreach (Type type in assembly.GetTypes())
            {
                if (type.GetCustomAttribute<LuaModuleAttribute>(false) == null) continue;
                registered += RegisterModule(type);
            }
            return registered;
        }

        public static void WhenReady(Action callback)
        {
            if (callback == null) return;
            bool invokeNow;
            lock (_lock)
            {
                invokeNow = _ready;
                if (!invokeNow) _readyCallbacks.Add(callback);
            }
            if (invokeNow) SafeInvoke(callback);
        }

        internal static void NotifyReady()
        {
            List<Action> callbacks;
            lock (_lock)
            {
                _ready = true;
                callbacks = new List<Action>(_readyCallbacks);
                _readyCallbacks.Clear();
            }
            foreach (Action callback in callbacks) SafeInvoke(callback);
        }

        internal static void BuildInto(Script script, Table rimRoot)
        {
            List<LuaApiEntry> snapshot;
            lock (_lock)
            {
                snapshot = new List<LuaApiEntry>(_entries.Values);
            }
            snapshot.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

            foreach (LuaApiEntry entry in snapshot)
            {
                string[] parts = entry.Path.Split('.');
                Table current = rimRoot;
                bool broken = false;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    DynValue existing = current.Get(parts[i]);
                    if (existing.IsNil())
                    {
                        var child = new Table(script);
                        current.Set(parts[i], DynValue.NewTable(child));
                        current = child;
                    }
                    else if (existing.Type == DataType.Table)
                    {
                        current = existing.Table;
                    }
                    else
                    {
                        Log.Error(LogTag + " path conflict at '" + entry.Path + "' (segment '" + parts[i] + "' is not a table)");
                        broken = true;
                        break;
                    }
                }
                if (broken) continue;
                try
                {
                    current.Set(parts[parts.Length - 1], entry.Build(script));
                }
                catch (Exception e)
                {
                    Log.Error(LogTag + " failed to build '" + entry.Path + "': " + e.Message);
                }
            }
        }

        private static int RegisterModule(Type moduleType, object instance)
        {
            LuaModuleAttribute moduleAttribute = moduleType.GetCustomAttribute<LuaModuleAttribute>(false);
            if (moduleAttribute == null)
            {
                Log.Error(LogTag + " type " + moduleType.FullName + " has no [LuaModule] attribute");
                return 0;
            }
            if (!ValidatePath(moduleAttribute.Namespace + ".probe", out _))
            {
                Log.Error(LogTag + " type " + moduleType.FullName + " has invalid [LuaModule] namespace '" + moduleAttribute.Namespace + "'");
                return 0;
            }

            int registered = 0;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            foreach (MethodInfo method in moduleType.GetMethods(flags))
            {
                LuaFunctionAttribute attribute = method.GetCustomAttribute<LuaFunctionAttribute>(false);
                if (attribute == null) continue;

                if (!method.IsStatic && instance == null)
                {
                    continue;
                }
                if (method.ContainsGenericParameters)
                {
                    Log.Error(LogTag + " skip generic method " + moduleType.FullName + "." + method.Name);
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (!ValidateSignature(moduleAttribute.Namespace + "." + method.Name, parameters, method.ReturnType, out _))
                {
                    continue;
                }

                string name = string.IsNullOrEmpty(attribute.Name) ? method.Name : attribute.Name;
                string path = moduleAttribute.Namespace + "." + name;
                object target = method.IsStatic ? null : instance;
                var entry = new LuaApiCallableEntry(path, parameters, method.ReturnType, args => method.Invoke(target, args));
                if (TryAdd(path, entry)) registered++;
            }

            foreach (FieldInfo field in moduleType.GetFields(flags))
            {
                LuaGlobalAttribute attribute = field.GetCustomAttribute<LuaGlobalAttribute>(false);
                if (attribute == null) continue;
                if (!field.IsStatic)
                {
                    continue;
                }

                string name = string.IsNullOrEmpty(attribute.Name) ? field.Name : attribute.Name;
                string path = moduleAttribute.Namespace + "." + name;
                var entry = new LuaApiValueEntry(path, field.GetValue(null));
                if (TryAdd(path, entry)) registered++;
            }

            foreach (PropertyInfo property in moduleType.GetProperties(flags))
            {
                LuaGlobalAttribute attribute = property.GetCustomAttribute<LuaGlobalAttribute>(false);
                if (attribute == null) continue;
                MethodInfo getter = property.GetGetMethod(true);
                if (getter == null) continue;
                if (!getter.IsStatic && instance == null) continue;

                object value = getter.Invoke(getter.IsStatic ? null : instance, null);
                string name = string.IsNullOrEmpty(attribute.Name) ? property.Name : attribute.Name;
                string path = moduleAttribute.Namespace + "." + name;
                var entry = new LuaApiValueEntry(path, value);
                if (TryAdd(path, entry)) registered++;
            }

            Log.Message(LogTag + " registered module " + moduleType.FullName + " (" + moduleAttribute.Namespace + "): " + registered + " entr" + (registered == 1 ? "y" : "ies"));
            return registered;
        }

        private static bool ValidateSignature(string path, ParameterInfo[] parameters, Type returnType, out int contextCount)
        {
            contextCount = 0;
            if (parameters.Length > 16)
            {
                Log.Error(LogTag + " " + path + " has too many parameters (max 16)");
                return false;
            }

            foreach (ParameterInfo parameter in parameters)
            {
                Type type = parameter.ParameterType;
                if (type == typeof(LuaApiContext))
                {
                    contextCount++;
                    if (contextCount > 1)
                    {
                        Log.Error(LogTag + " " + path + " has more than one LuaApiContext parameter");
                        return false;
                    }
                    continue;
                }
                if (type.IsByRef || parameter.IsOut)
                {
                    Log.Error(LogTag + " " + path + " uses ref/out parameter '" + parameter.Name + "' (not supported)");
                    return false;
                }
                if (!LuaValueConverter.IsSupportedParameter(type))
                {
                    Log.Error(LogTag + " " + path + " parameter '" + parameter.Name + "' has unsupported type " + type.FullName);
                    return false;
                }
            }

            if (!LuaValueConverter.IsSupportedReturn(returnType))
            {
                Log.Error(LogTag + " " + path + " return type " + returnType.FullName + " is not supported");
                return false;
            }
            return true;
        }

        private static bool ValidatePath(string path, out string[] parts)
        {
            parts = null;
            if (string.IsNullOrEmpty(path))
            {
                Log.Error(LogTag + " empty API path");
                return false;
            }
            if (path.Length > 96)
            {
                Log.Error(LogTag + " API path too long: " + path);
                return false;
            }

            parts = path.Split('.');
            if (parts.Length > 8)
            {
                Log.Error(LogTag + " API path has too many segments: " + path);
                return false;
            }
            foreach (string segment in parts)
            {
                if (segment.Length == 0 || segment.Length > 32)
                {
                    Log.Error(LogTag + " invalid API path segment in '" + path + "'");
                    return false;
                }
                if (!(char.IsLetter(segment[0]) || segment[0] == '_'))
                {
                    Log.Error(LogTag + " API path segment must start with a letter or underscore: " + path);
                    return false;
                }
                foreach (char c in segment)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '_'))
                    {
                        Log.Error(LogTag + " API path segment contains invalid character: " + path);
                        return false;
                    }
                }
            }

            if (_reservedRoots.Contains(parts[0]))
            {
                Log.Error(LogTag + " API path uses reserved name '" + parts[0] + "': " + path);
                return false;
            }
            return true;
        }

        private static bool TryAdd(string path, LuaApiEntry entry)
        {
            if (!ValidatePath(path, out _)) return false;

            lock (_lock)
            {
                foreach (string existing in _entries.Keys)
                {
                    if (existing == path || path.StartsWith(existing + ".", StringComparison.Ordinal) ||
                        existing.StartsWith(path + ".", StringComparison.Ordinal))
                    {
                        Log.Error(LogTag + " API path conflict: '" + path + "' conflicts with '" + existing + "'");
                        return false;
                    }
                }
                _entries[path] = entry;
            }
            return true;
        }

        private static void SafeInvoke(Action callback)
        {
            try
            {
                callback();
            }
            catch (Exception e)
            {
                Log.Error(LogTag + " WhenReady callback failed: " + e);
            }
        }
    }
}
