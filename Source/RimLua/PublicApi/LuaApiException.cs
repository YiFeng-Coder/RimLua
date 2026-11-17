using System;

namespace RimLua.Api
{
    /// <summary>第三方 API 注册/参数/返回值错误。</summary>
    public class LuaApiException : Exception
    {
        public LuaApiException(string message) : base(message)
        {
        }

        public LuaApiException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
