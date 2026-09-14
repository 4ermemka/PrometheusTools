using Assets.Shared.SyncSystem.Core;
using Newtonsoft.Json;
using System.Text;

namespace Assets.Scripts.Network.NetCore
{
    public static class JsonGameSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                IgnoreSerializableAttribute = false
            }
        };

        public static string Serialize(object obj)
        {
            return JsonConvert.SerializeObject(obj, Settings);
        }

        public static byte[] SerializeToBytes(object obj)
        {
            return Encoding.UTF8.GetBytes(Serialize(obj));
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        public static T Deserialize<T>(byte[] bytes)
        {
            return Deserialize<T>(Encoding.UTF8.GetString(bytes));
        }

        public static T ConvertValue<T>(object value)
        {
            return SyncValueConverter.ConvertValue<T>(value);
        }
    }
}
