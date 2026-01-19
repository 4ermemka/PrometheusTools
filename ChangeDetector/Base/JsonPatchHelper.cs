using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector.Serialization
{
    public static class JsonPatchHelper
    {
        public static object? ConvertPatchValue(object? value, Type targetType)
        {
            if (value == null) return null;

            // Если value уже правильного типа
            if (targetType.IsInstanceOfType(value))
                return value;

            // Если это JObject, десериализуем в targetType
            if (value is JObject jObject)
            {
                try
                {
                    return jObject.ToObject(targetType);
                }
                catch (Exception ex)
                {
                    throw new InvalidCastException($"Failed to convert JObject to {targetType.Name}: {ex.Message}");
                }
            }

            // Если это JArray, десериализуем в список
            if (value is JArray jArray && targetType.IsGenericType &&
                targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                try
                {
                    var elementType = targetType.GetGenericArguments()[0];
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    return jArray.ToObject(listType);
                }
                catch (Exception ex)
                {
                    throw new InvalidCastException($"Failed to convert JArray to List: {ex.Message}");
                }
            }

            // Пытаемся стандартную конвертацию
            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                return value;
            }
        }
    }
}