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

            // 1) Если это JObject – пробуем развернуть в один из известных DTO
            if (value is JObject jObj)
            {
                foreach (var dtoType in SyncValueMapperRegistry.GetAllDtoTypes())
                {
                    try
                    {
                        var dtoInstance = jObj.ToObject(dtoType);
                        if (dtoInstance != null)
                        {
                            value = dtoInstance;
                            break;
                        }
                    }
                    catch
                    {
                        // игнорируем и пробуем следующий тип
                    }
                }
            }

            // 2) После этого пробуем маппер DTO → модель
            var type = value.GetType();
            if (SyncValueMapperRegistry.TryGetMapperForDto(type, out var mapper2))
                return mapper2.FromDto(value);

            return value;
        }
    }
}