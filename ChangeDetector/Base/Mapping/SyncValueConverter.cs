using Newtonsoft.Json.Linq;

namespace Assets.Shared.ChangeDetector.Base.Mapping
{

    public static class SyncValueConverter
    {
        public static object ToDtoIfNeeded(object value)
        {
            if (value == null)
                return null;

            var type = value.GetType();
            if (SyncValueMapperRegistry.TryGetMapperForModel(type, out var mapper))
                return mapper.ToDto(value);

            return value;
        }

        public static object FromDtoIfNeeded(object value)
        {
            if (value == null)
                return null;

            // 1) Если это JObject – сначала преобразуем его в один из DTO, которые знает реестр
            if (value is JObject jObj)
            {
                // перебираем все известные DTO-типы
                foreach (var dtoType in SyncValueMapperRegistry.GetAllDtoTypes())
                {
                    try
                    {
                        // пробуем десериализовать JObject в этот DTO
                        var dtoInstance = jObj.ToObject(dtoType);
                        if (dtoInstance != null)
                        {
                            value = dtoInstance;
                            break;
                        }
                    }
                    catch
                    {
                        // просто пробуем следующий тип
                    }
                }
            }

            // 2) После этого пробуем маппер
            var type = value.GetType();
            if (SyncValueMapperRegistry.TryGetMapperForDto(type, out var mapper2))
                return mapper2.FromDto(value);

            return value;
        }
    }


}