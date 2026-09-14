using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using UnityEngine;

namespace Assets.Shared.SyncSystem.Core
{
    /// <summary>
    /// Central conversion helper used by Sync values when data arrives from JSON/network snapshots.
    /// It intentionally lives in SyncSystem so synced data classes do not depend on NetworkSystem.
    /// </summary>
    public static class SyncValueConverter
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static T ConvertValue<T>(object value)
        {
            if (value == null) return default;

            if (value is T typedValue)
                return typedValue;

            if (TryConvertUnityValue(value, out T unityValue))
                return unityValue;

            if (value is JToken token)
                return token.ToObject<T>(JsonSerializer.Create(Settings));

            Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (targetType.IsEnum)
                return (T)Enum.Parse(targetType, value.ToString());

            if (targetType.IsPrimitive || targetType == typeof(string) || targetType == typeof(decimal))
                return (T)Convert.ChangeType(value, targetType);

            string json = JsonConvert.SerializeObject(value, Settings);
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        private static bool TryConvertUnityValue<T>(object value, out T converted)
        {
            converted = default;

            if (typeof(T) == typeof(Vector2) && TryReadFloat(value, "x", out float x2) && TryReadFloat(value, "y", out float y2))
            {
                converted = (T)(object)new Vector2(x2, y2);
                return true;
            }

            if (typeof(T) == typeof(Vector3) &&
                TryReadFloat(value, "x", out float x3) &&
                TryReadFloat(value, "y", out float y3) &&
                TryReadFloat(value, "z", out float z3))
            {
                converted = (T)(object)new Vector3(x3, y3, z3);
                return true;
            }

            if (typeof(T) == typeof(Quaternion) &&
                TryReadFloat(value, "x", out float qx) &&
                TryReadFloat(value, "y", out float qy) &&
                TryReadFloat(value, "z", out float qz) &&
                TryReadFloat(value, "w", out float qw))
            {
                converted = (T)(object)new Quaternion(qx, qy, qz, qw);
                return true;
            }

            return false;
        }

        private static bool TryReadFloat(object source, string name, out float value)
        {
            value = default;

            if (source is JObject obj && obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
            {
                value = token.Value<float>();
                return true;
            }

            var property = source.GetType().GetProperty(name);
            if (property != null)
            {
                value = Convert.ToSingle(property.GetValue(source));
                return true;
            }

            var field = source.GetType().GetField(name);
            if (field != null)
            {
                value = Convert.ToSingle(field.GetValue(source));
                return true;
            }

            return false;
        }
    }
}
