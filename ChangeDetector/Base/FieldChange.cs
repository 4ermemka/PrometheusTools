using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Описание изменения поля/свойства.
    /// Path — путь от корня до изменённого поля.
    /// </summary>
    public class FieldChange
    {
        public IReadOnlyList<FieldPathSegment> Path { get; }
        public object OldValue { get; }
        public object NewValue { get; }

        public FieldChange(IReadOnlyList<FieldPathSegment> path, object oldValue, object newValue)
        {
            Path = path;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
}