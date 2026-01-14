using System.Collections;
using UnityEngine;

namespace Assets.Shared.ChangeDetector.Base
{
    public interface ISyncValueMapper
    {
        // модель → DTO
        object ToDto(object modelValue);

        // DTO → модель
        object FromDto(object dtoValue);
    }

}