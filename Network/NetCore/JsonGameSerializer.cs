using Newtonsoft.Json;
using System.Text;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// JSON-сериализатор для сетевых сообщений и состояния.
    /// Подходит для отладки; для продакшена лучше взять MessagePack или свой бинарный формат.
    /// </summary>
    public static class JsonGameSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // Игнорировать циклические ссылки
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            
            // Не сериализовать null значения
            NullValueHandling = NullValueHandling.Ignore,
            
            // Форматирование для отладки
            Formatting = Formatting.Indented,

            TypeNameHandling = TypeNameHandling.None, // Не добавлять информацию о типе
            
            // Игнорировать поля/свойства с атрибутом [JsonIgnore]
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                IgnoreSerializableAttribute = false
            }
        };

        public static byte[] Serialize<T>(T obj)
        {
            var json = JsonConvert.SerializeObject(obj, Settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public static T Deserialize<T>(byte[] bytes)
        {
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }
}