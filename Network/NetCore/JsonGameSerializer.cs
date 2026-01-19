using Assets.Scripts.Network.NetCore.Assets.Shared.ChangeDetector.Serialization;
using Assets.Shared.ChangeDetector;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Assets.Scripts.Network.NetCore
{

    namespace Assets.Shared.ChangeDetector.Serialization
    {
        public class SyncPropertyConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType.IsGenericType &&
                       objectType.GetGenericTypeDefinition() == typeof(SyncProperty<>);
            }

            public override object ReadJson(JsonReader reader, Type objectType,
                object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null)
                    return null;

                // Получаем значение из JSON
                var value = serializer.Deserialize(reader, objectType.GetGenericArguments()[0]);

                // Если существующее значение null, создаем новый SyncProperty
                if (existingValue == null)
                {
                    // Не можем создать SyncProperty<T> без владельца, 
                    // так что возвращаем только значение
                    return value;
                }

                // Если SyncProperty уже существует, обновляем его Value
                var valueProperty = objectType.GetProperty("Value");
                if (valueProperty != null && valueProperty.CanWrite)
                {
                    valueProperty.SetValue(existingValue, value);
                }

                return existingValue;
            }

            public override void WriteJson(JsonWriter writer, object value,
                JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }

                var valueProperty = value.GetType().GetProperty("Value");
                if (valueProperty == null)
                {
                    writer.WriteNull();
                    return;
                }

                var innerValue = valueProperty.GetValue(value);
                serializer.Serialize(writer, innerValue);
            }
        }
    }
    /// <summary>
    /// JSON-сериализатор для сетевых сообщений и состояния.
    /// Подходит для отладки; для продакшена лучше взять MessagePack или свой бинарный формат.
    /// </summary>
    public sealed class JsonGameSerializer : IGameSerializer
    {
        private readonly JsonSerializerSettings _settings;

        public JsonGameSerializer()
        {
            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Formatting = Formatting.None,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                },
                Converters = new List<JsonConverter>
            {
                new SyncPropertyConverter()
            }
            };
        }

        public byte[] Serialize<T>(T obj)
        {
            var json = JsonConvert.SerializeObject(obj, _settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public T Deserialize<T>(byte[] data)
        {
            var json = Encoding.UTF8.GetString(data);
            return JsonConvert.DeserializeObject<T>(json, _settings);
        }
    }
}