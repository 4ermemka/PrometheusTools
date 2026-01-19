using Assets.Shared.ChangeDetector;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Assets.Scripts.Network.NetCore
{
    // Конвертер для SyncProperty
    public class SyncPropertyValueConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType.IsGenericType &&
                   objectType.GetGenericTypeDefinition() == typeof(SyncProperty<>);
        }

        public override object ReadJson(JsonReader reader, Type objectType,
            object existingValue, JsonSerializer serializer)
        {
            // Просто читаем значение, SyncProperty будет инициализирован позже
            var valueType = objectType.GetGenericArguments()[0];
            return serializer.Deserialize(reader, valueType);
        }

        public override void WriteJson(JsonWriter writer, object value,
            JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            // Пишем только Value из SyncProperty
            var valueProperty = value.GetType().GetProperty("Value");
            if (valueProperty != null)
            {
                var innerValue = valueProperty.GetValue(value);
                serializer.Serialize(writer, innerValue);
            }
            else
            {
                writer.WriteNull();
            }
        }
    }

    /// <summary>
    /// JSON-сериализатор для сетевых сообщений и состояния.
    /// Подходит для отладки; для продакшена лучше взять MessagePack или свой бинарный формат.
    /// </summary>
    public sealed class JsonGameSerializer : IGameSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto, // ИЗМЕНИТЬ с None на Auto или All
            Converters = new List<JsonConverter>
        {
            new SyncPropertyValueConverter() // Добавьте этот конвертер
        }
        };

        public byte[] Serialize<T>(T obj)
        {
            var json = JsonConvert.SerializeObject(obj, Settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public T Deserialize<T>(byte[] bytes)
        {
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }
}