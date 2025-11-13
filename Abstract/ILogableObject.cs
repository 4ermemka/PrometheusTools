using PrometheusTools.Shared.Enums;
using System;

namespace PrometheusTools.Shared.Abstract
{
    public interface ILogableObject
    {
        string Name { get; set; }
        //Type, Message
        Action<LogType, string> OnLog { get; set; }
    }
}
