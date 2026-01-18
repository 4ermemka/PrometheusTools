using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Base.Mapping;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Assets.Scripts.Network.NetCore
{
    public sealed class JsonGameSerializer : IGameSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented
        };

        public byte[] Serialize<T>(T obj)
        {
            // Если это SyncNode - используем универсальную сериализацию
            if (obj is SyncNode syncNode)
            {
                return SerializeSyncNode(syncNode);
            }

            // Обычная сериализация для других типов
            var json = JsonConvert.SerializeObject(obj, Settings);
            return Encoding.UTF8.GetBytes(json);
        }

        private byte[] SerializeSyncNode(SyncNode node)
        {
            // Рекурсивно собираем все данные
            var data = CollectSyncNodeData(node);
            var json = JsonConvert.SerializeObject(data, Settings);
            return Encoding.UTF8.GetBytes(json);
        }

        private Dictionary<string, object> CollectSyncNodeData(SyncNode node)
        {
            var result = new Dictionary<string, object>();
            var type = node.GetType();

            // Используем рефлексию для получения всех [SyncField] свойств
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var prop in properties)
            {
                var syncFieldAttr = prop.GetCustomAttribute<SyncFieldAttribute>();
                if (syncFieldAttr == null)
                    continue;

                var value = prop.GetValue(node);
                if (value == null)
                    continue;

                // Конвертируем значение через SyncValueConverter
                var convertedValue = ConvertValueForSerialization(value);
                result[prop.Name] = convertedValue;
            }

            return result;
        }

        private object ConvertValueForSerialization(object value)
        {
            var valueType = value.GetType();

            // SyncProperty<T> - извлекаем Value
            if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                var valueProperty = valueType.GetProperty("Value");
                var actualValue = valueProperty?.GetValue(value);
                return SyncValueConverter.ToDtoIfNeeded(actualValue);
            }
            // SyncList<T> или другая коллекция
            else if (value is IEnumerable enumerable && !(value is string))
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                {
                    if (item is SyncNode childNode)
                    {
                        list.Add(CollectSyncNodeData(childNode));
                    }
                    else
                    {
                        list.Add(SyncValueConverter.ToDtoIfNeeded(item));
                    }
                }
                return list;
            }
            // Вложенный SyncNode
            else if (value is SyncNode childNode)
            {
                return CollectSyncNodeData(childNode);
            }
            // Любой другой тип
            else
            {
                return SyncValueConverter.ToDtoIfNeeded(value);
            }
        }

        public T Deserialize<T>(byte[] bytes)
        {
            var json = Encoding.UTF8.GetString(bytes);

            // Если T - SyncNode или его наследник, используем специальную десериализацию
            if (typeof(SyncNode).IsAssignableFrom(typeof(T)))
            {
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json, Settings);
                var node = DeserializeSyncNode(typeof(T), data);

                if (node is T result)
                {
                    return result;
                }

                throw new InvalidOperationException($"Не удалось преобразовать SyncNode в тип {typeof(T)}");
            }

            // Обычная десериализация
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        private SyncNode DeserializeSyncNode(Type nodeType, Dictionary<string, object> data)
        {
            var instance = (SyncNode)Activator.CreateInstance(nodeType);
            ApplyDataToSyncNode(instance, data);
            return instance;
        }

        private void ApplyDataToSyncNode(SyncNode node, Dictionary<string, object> data)
        {
            var type = node.GetType();

            foreach (var kvp in data)
            {
                var prop = type.GetProperty(kvp.Key, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop == null)
                    continue;

                var syncFieldAttr = prop.GetCustomAttribute<SyncFieldAttribute>();
                if (syncFieldAttr == null)
                    continue;

                var value = ConvertValueFromDeserialization(prop.PropertyType, kvp.Value);
                if (value != null)
                {
                    prop.SetValue(node, value);
                }
            }
        }

        private object ConvertValueFromDeserialization(Type targetType, object data)
        {
            // SyncProperty<T>
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                // Значение для SyncProperty (сам SyncProperty создается в конструкторе)
                var valueType = targetType.GetGenericArguments()[0];
                return SyncValueConverter.FromDtoIfNeeded(data);
            }
            // SyncList<T>
            else if (typeof(IEnumerable).IsAssignableFrom(targetType) && targetType.IsGenericType)
            {
                var itemType = targetType.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(itemType);
                var list = (IList)Activator.CreateInstance(listType);

                if (data is IEnumerable enumerable)
                {
                    foreach (var itemData in enumerable)
                    {
                        object item;
                        if (typeof(SyncNode).IsAssignableFrom(itemType))
                        {
                            item = DeserializeSyncNode(itemType, (Dictionary<string, object>)itemData);
                        }
                        else
                        {
                            item = SyncValueConverter.FromDtoIfNeeded(itemData);
                        }
                        list.Add(item);
                    }
                }

                return list;
            }
            // Вложенный SyncNode
            else if (typeof(SyncNode).IsAssignableFrom(targetType))
            {
                return DeserializeSyncNode(targetType, (Dictionary<string, object>)data);
            }
            // Любой другой тип
            else
            {
                return SyncValueConverter.FromDtoIfNeeded(data);
            }
        }
    }
}