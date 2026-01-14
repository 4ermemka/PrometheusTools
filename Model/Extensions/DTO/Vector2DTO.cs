using Assets.Shared.ChangeDetector.Base;
using UnityEngine;

[SyncDto(typeof(Vector2))]
public sealed class Vector2Dto : ISyncValueMapper
{
    public float x { get; set; }
    public float y { get; set; }

    public object ToDto(object modelValue)
    {
        var v = (Vector2)modelValue;
        return new Vector2Dto { x = v.x, y = v.y };
    }

    public object FromDto(object dtoValue)
    {
        var dto = (Vector2Dto)dtoValue;
        return new Vector2(dto.x, dto.y);
    }
}

