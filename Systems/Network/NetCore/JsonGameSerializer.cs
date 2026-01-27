using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace Assets.Scripts.Network.NetCore
{
    public static class JsonGameSerializer
    {
        private static JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            // Важно: НЕ добавляем информацию о типах
            TypeNameHandling = TypeNameHandling.None,

            // Для корректной работы с Unity типами
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,

            // Используем DefaultContractResolver для Unity-совместимости
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                IgnoreSerializableAttribute = false
            }
        };

        /// <summary>
        /// Сериализация объекта в JSON строку
        /// </summary>
        public static string Serialize(object obj)
        {
            return JsonConvert.SerializeObject(obj, _settings);
        }

        /// <summary>
        /// Сериализация объекта в байты (UTF8)
        /// </summary>
        public static byte[] SerializeToBytes(object obj)
        {
            string json = Serialize(obj);
            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Десериализация JSON строки в указанный тип
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, _settings);
        }

        /// <summary>
        /// Десериализация байтов (UTF8) в указанный тип
        /// </summary>
        public static T Deserialize<T>(byte[] bytes)
        {
            string json = Encoding.UTF8.GetString(bytes);
            return Deserialize<T>(json);
        }

        /// <summary>
        /// Универсальное преобразование object -> T для Sync<T>
        /// </summary>
        public static T ConvertValue<T>(object value)
        {
            if (value == null) return default;

            // Если уже нужный тип
            if (value is T typedValue)
                return typedValue;

            // Если примитивный тип или строка
            if (typeof(T).IsPrimitive || typeof(T) == typeof(string) || typeof(T) == typeof(decimal))
            {
                try
                {
                    return (T)System.Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return default;
                }
            }

            // Если JToken (пришло из сети)
            if (value is JToken jToken)
            {
                return jToken.ToObject<T>();
            }

            // Для кастомных типов - используем JSON как промежуточный формат
            try
            {
                string json = Serialize(value);
                return Deserialize<T>(json);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Failed to convert value to {typeof(T).Name}: {ex.Message}");
                return default;
            }
        }
    }
}