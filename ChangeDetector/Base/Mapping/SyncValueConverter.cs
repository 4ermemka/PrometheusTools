using System.Collections;
using UnityEngine;

namespace Assets.Shared.ChangeDetector.Base.Mapping
{
    public static class SyncValueConverter
    {
        // модель → то, что кладём в PatchMessage.NewValue
        public static object ToDtoIfNeeded(object value)
        {
            if (value == null)
                return null;

            var type = value.GetType();
            if (SyncValueMapperRegistry.TryGetMapperForModel(type, out var mapper))
                return mapper.ToDto(value); // вернёт DTO (например, Vector2Dto)

            return value; // без маппинга
        }

        // значение из PatchMessage.NewValue → модель
        public static object FromDtoIfNeeded(object value)
        {
            if (value == null)
                return null;

            var type = value.GetType();
            if (SyncValueMapperRegistry.TryGetMapperForDto(type, out var mapper))
                return mapper.FromDto(value); // вернёт модельный тип (например, Vector2)

            return value;
        }
    }

}