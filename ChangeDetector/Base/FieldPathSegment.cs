using System;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Сегмент пути до изменённого поля (имя свойства/поля).
    /// </summary>
    public struct FieldPathSegment
    {
        public string Name { get; }

        public FieldPathSegment(string name)
        {
            Name = name;
        }

        public override string ToString() => Name;
    }
}