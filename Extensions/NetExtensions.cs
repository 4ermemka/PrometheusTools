using Newtonsoft.Json;
using PrometheusTools.Shared.Models;
using System.Text;

public static class NetExtensions
{
    public static byte[] ToBuffer(this NetMsg msg)
    {
        var stringMessage = JsonConvert.SerializeObject(msg, Formatting.Indented);
        var bytes = Encoding.UTF8.GetBytes(stringMessage);

        return bytes;
    }

    public static NetMsg ToNetMsg(this byte[] bytes)
    {
        JsonSerializerSettings settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
        string Serialized = JsonConvert.SerializeObject(bytes, settings);

        NetMsg msg = JsonConvert.DeserializeObject<NetMsg>(Serialized, settings);
        return msg;
    }
}
