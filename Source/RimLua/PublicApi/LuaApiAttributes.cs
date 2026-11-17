using System;

namespace RimLua.Api
{
    /// <summary>标记一个类为 Lua API 模块；Namespace 是注册到 rim 下的命名空间。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class LuaModuleAttribute : Attribute
    {
        public string Namespace { get; private set; }

        public LuaModuleAttribute(string moduleNamespace)
        {
            Namespace = moduleNamespace;
        }
    }

    /// <summary>标记模块中的 public static 方法为 Lua 函数。</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class LuaFunctionAttribute : Attribute
    {
        public string Name { get; private set; }

        public LuaFunctionAttribute(string name = null)
        {
            Name = name;
        }
    }

    /// <summary>标记模块中的 public static 字段/属性为 Lua 常量或值。</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class LuaGlobalAttribute : Attribute
    {
        public string Name { get; private set; }

        public LuaGlobalAttribute(string name = null)
        {
            Name = name;
        }
    }
}
