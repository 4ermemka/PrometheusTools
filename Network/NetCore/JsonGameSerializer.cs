using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
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
            TypeNameHandling = TypeNameHandling.None, // Полностью отключаем
            Formatting = Formatting.None,
            Converters = new List<JsonConverter>
        {
            new SyncPropertyValueConverter(),
            new SyncListConverter() // Специальный конвертер для SyncList
        }
        };

        public byte[] Serialize<T>(T obj)
        {
            // Если это SyncNode, используем GetSerializableData
            if (obj is SyncNode syncNode)
            {
                var data = syncNode.GetSerializableData();
                var jsonData = JsonConvert.SerializeObject(data, Settings);
                return Encoding.UTF8.GetBytes(jsonData);
            }

            var json = JsonConvert.SerializeObject(obj, Settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public T Deserialize<T>(byte[] bytes)
        {
            var json = Encoding.UTF8.GetString(bytes);

            // Если T это SyncNode, десериализуем в словарь
            if (typeof(SyncNode).IsAssignableFrom(typeof(T)))
            {
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json, Settings);
                if (data == null)
                    throw new InvalidOperationException("Failed to deserialize");

                var instance = Activator.CreateInstance<T>();
                if (instance is SyncNode syncNode)
                {
                    syncNode.ApplySerializedData(data);
                    return instance;
                }
            }

            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }

    public class SyncListConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType.IsGenericType &&
                   objectType.GetGenericTypeDefinition() == typeof(SyncList<>);
        }

        public override object ReadJson(JsonReader reader, Type objectType,
            object existingValue, JsonSerializer serializer)
        {
            var elementType = objectType.GetGenericArguments()[0];
            var listType = typeof(List<>).MakeGenericType(elementType);
            var items = serializer.Deserialize(reader, listType);

            // Создаем SyncList
            var syncList = Activator.CreateInstance(objectType) as System.Collections.IList;
            if (syncList != null && items is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    syncList.Add(item);
                }
            }

            return syncList;
        }

        public override void WriteJson(JsonWriter writer, object value,
            JsonSerializer serializer)
        {
            if (value is System.Collections.IEnumerable enumerable)
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                {
                    list.Add(item);
                }
                serializer.Serialize(writer, list);
            }
            else
            {
                writer.WriteNull();
            }
        }
    }
}