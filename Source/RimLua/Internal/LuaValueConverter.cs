using System;
using System.Collections;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using RimLua.Api;
using Verse;

namespace RimLua.Internal
{
    /// <summary>Lua 值与 C# 值的安全编解码。只支持白名单类型，禁止把任意 CLR 对象暴露给 Lua。</summary>
    internal static class LuaValueConverter
    {
        public static bool IsSupportedParameter(Type type)
        {
            if (type == typeof(LuaApiContext)) return true;
            if (type == typeof(DynValue) || type == typeof(Table)) return true;
            if (type == typeof(string) || type == typeof(bool) || type == typeof(object)) return true;
            if (type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double) ||
                type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) ||
                type == typeof(ushort) || type == typeof(sbyte) || type == typeof(decimal)) return true;
            if (type.IsEnum) return true;
            if (type == typeof(IntVec3)) return true;
            Type nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) return IsSupportedParameter(nullable);
            if (type.IsArray) return IsSupportedParameter(type.GetElementType());
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return IsSupportedParameter(type.GetGenericArguments()[0]);
            return false;
        }

        public static bool IsSupportedReturn(Type type)
        {
            if (type == typeof(void)) return true;
            if (type == typeof(DynValue) || type == typeof(Table)) return true;
            if (type == typeof(string) || type == typeof(bool)) return true;
            if (type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double) ||
                type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) ||
                type == typeof(ushort) || type == typeof(sbyte) || type == typeof(decimal)) return true;
            if (type.IsEnum) return true;
            if (type == typeof(IntVec3)) return true;
            if (typeof(IDictionary).IsAssignableFrom(type)) return true;
            if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string)) return true;
            return false;
        }

        public static object ToClr(DynValue value, Type targetType, string apiName)
        {
            if (targetType == typeof(DynValue)) return value;
            if (targetType == typeof(Table))
            {
                if (value.Type != DataType.Table)
                    throw Error(apiName, "expected table, got " + value.Type);
                return value.Table;
            }

            Type nullable = Nullable.GetUnderlyingType(targetType);
            if (nullable != null)
            {
                if (value.IsNil()) return null;
                return ToClr(value, nullable, apiName);
            }

            if (value.IsNil())
            {
                if (!targetType.IsValueType) return null;
                throw Error(apiName, "nil is not valid for " + targetType.Name);
            }

            if (targetType == typeof(object))
            {
                if (value.Type == DataType.UserData)
                    throw Error(apiName, "userdata is not accepted as object");
                return value.ToObject();
            }
            if (targetType == typeof(string)) return value.CastToString();
            if (targetType == typeof(bool))
            {
                if (value.Type != DataType.Boolean) throw Error(apiName, "expected boolean, got " + value.Type);
                return value.Boolean;
            }
            if (targetType == typeof(int)) return checked((int)ToIntegral(value, apiName));
            if (targetType == typeof(long)) return checked((long)ToIntegral(value, apiName));
            if (targetType == typeof(short)) return checked((short)ToIntegral(value, apiName));
            if (targetType == typeof(byte)) return checked((byte)ToIntegral(value, apiName));
            if (targetType == typeof(uint)) return checked((uint)ToIntegral(value, apiName));
            if (targetType == typeof(ulong)) return checked((ulong)ToIntegral(value, apiName));
            if (targetType == typeof(ushort)) return checked((ushort)ToIntegral(value, apiName));
            if (targetType == typeof(sbyte)) return checked((sbyte)ToIntegral(value, apiName));
            if (targetType == typeof(float)) return (float)ToNumber(value, apiName);
            if (targetType == typeof(double)) return ToNumber(value, apiName);
            if (targetType == typeof(decimal)) return (decimal)ToNumber(value, apiName);

            if (targetType.IsEnum)
            {
                if (value.Type == DataType.String)
                {
                    try { return Enum.Parse(targetType, value.String, true); }
                    catch { throw Error(apiName, "invalid enum value '" + value.String + "' for " + targetType.Name); }
                }
                if (value.Type == DataType.Number)
                {
                    object numeric = Enum.ToObject(targetType, checked((long)value.Number));
                    return numeric;
                }
                throw Error(apiName, "enum parameter expects string or number");
            }

            if (targetType == typeof(IntVec3))
            {
                if (value.Type != DataType.Table) throw Error(apiName, "IntVec3 expects table {x,y,z}");
                Table table = value.Table;
                int x = (int)ToClr(table.Get("x"), typeof(int), apiName);
                int y = (int)ToClr(table.Get("y"), typeof(int), apiName);
                int z = (int)ToClr(table.Get("z"), typeof(int), apiName);
                return new IntVec3(x, y, z);
            }

            if (targetType.IsArray)
            {
                Type element = targetType.GetElementType();
                List<DynValue> items = ToList(value, apiName);
                Array array = Array.CreateInstance(element, items.Count);
                for (int i = 0; i < items.Count; i++) array.SetValue(ToClr(items[i], element, apiName), i);
                return array;
            }

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type element = targetType.GetGenericArguments()[0];
                List<DynValue> items = ToList(value, apiName);
                IList list = (IList)Activator.CreateInstance(targetType);
                for (int i = 0; i < items.Count; i++) list.Add(ToClr(items[i], element, apiName));
                return list;
            }

            throw Error(apiName, "unsupported parameter type " + targetType.FullName);
        }

        public static DynValue FromClr(Script script, object value, string apiName)
        {
            if (value == null) return DynValue.Nil;
            if (value is DynValue) return (DynValue)value;
            if (value is Table) return DynValue.NewTable((Table)value);
            if (value is string) return DynValue.NewString((string)value);
            if (value is bool) return DynValue.NewBoolean((bool)value);
            if (value is Enum) return DynValue.NewString(value.ToString());
            if (value is IntVec3)
            {
                IntVec3 cell = (IntVec3)value;
                var table = new Table(script);
                table.Set("x", DynValue.NewNumber(cell.x));
                table.Set("y", DynValue.NewNumber(cell.y));
                table.Set("z", DynValue.NewNumber(cell.z));
                return DynValue.NewTable(table);
            }
            if (IsNumeric(value))
            {
                return DynValue.NewNumber(Convert.ToDouble(value));
            }
            if (value is IDictionary)
            {
                var table = new Table(script);
                foreach (DictionaryEntry entry in (IDictionary)value)
                {
                    string key = Convert.ToString(entry.Key);
                    table.Set(key, FromClr(script, entry.Value, apiName));
                }
                return DynValue.NewTable(table);
            }
            if (value is IEnumerable && !(value is string))
            {
                var table = new Table(script);
                foreach (object item in (IEnumerable)value)
                {
                    table.Append(FromClr(script, item, apiName));
                }
                return DynValue.NewTable(table);
            }
            throw new LuaApiException("[" + apiName + "] unsupported return type " + value.GetType().FullName);
        }

        private static bool IsNumeric(object value)
        {
            return value is sbyte || value is byte || value is short || value is ushort || value is int || value is uint ||
                   value is long || value is ulong || value is float || value is double || value is decimal;
        }

        private static List<DynValue> ToList(DynValue value, string apiName)
        {
            if (value.Type != DataType.Table) throw Error(apiName, "expected table/array, got " + value.Type);
            var items = new List<DynValue>();
            Table table = value.Table;
            for (int i = 1; i <= table.Length; i++)
            {
                items.Add(table.Get(i));
            }
            return items;
        }

        private static double ToNumber(DynValue value, string apiName)
        {
            if (value.Type != DataType.Number) throw Error(apiName, "expected number, got " + value.Type);
            return value.Number;
        }

        private static long ToIntegral(DynValue value, string apiName)
        {
            double number = ToNumber(value, apiName);
            if (Math.Abs(number - Math.Round(number)) > 0.000001)
                throw Error(apiName, "expected integer, got " + number);
            return checked((long)Math.Round(number));
        }

        private static LuaApiException Error(string apiName, string message)
        {
            return new LuaApiException("[" + apiName + "] " + message);
        }
    }
}
