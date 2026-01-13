using System;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Сегмент пути до изменённого поля (имя свойства/поля).
    /// </summary>
    public sealed class FieldPathSegment
    {
        public string Name { get; }

        public FieldPathSegment(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public override string ToString() => Name;
    }
}