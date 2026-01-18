namespace Assets.Shared.ChangeDetector.Base.Mapping
{
    // DTO для SyncProperty значения
    public class SyncPropertyDto
    {
        public object? Value { get; set; }
        public string TypeName { get; set; }
    }

    // Маппер для SyncProperty
    [SyncDto(typeof(SyncProperty<>))]
    public sealed class SyncPropertyMapper : ISyncValueMapper
    {
        public object ToDto(object modelValue)
        {
            var propType = modelValue.GetType();
            var valueProperty = propType.GetProperty("Value");
            var value = valueProperty?.GetValue(modelValue);

            return new SyncPropertyDto
            {
                Value = SyncValueConverter.ToDtoIfNeeded(value),
                TypeName = propType.GenericTypeArguments[0].AssemblyQualifiedName
            };
        }

        public object FromDto(object dtoValue)
        {
            var dto = (SyncPropertyDto)dtoValue;
            // SyncProperty воссоздается автоматически SyncNode
            return SyncValueConverter.FromDtoIfNeeded(dto.Value);
        }
    }
}