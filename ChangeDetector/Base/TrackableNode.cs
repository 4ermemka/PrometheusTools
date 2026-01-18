using System;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Базовый класс для всех отслеживаемых объектов.
    /// Упрощенная версия для работы с новой системой SyncProperty.
    /// </summary>
    public abstract class TrackableNode
    {
        /// <summary>
        /// Событие изменения любого отслеживаемого поля.
        /// </summary>
        public event Action<FieldChange>? Changed;

        /// <summary>
        /// Единая точка выхода изменений наружу.
        /// </summary>
        protected virtual void RaiseChange(FieldChange change)
        {
            Changed?.Invoke(change);
        }

        /// <summary>
        /// Универсальный сеттер для простых полей (для обратной совместимости).
        /// </summary>
        protected bool SetProperty<T>(
            ref T field,
            T value,
            [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
                return false;

            var oldValue = field;
            field = value;

            var path = new List<FieldPathSegment> { new FieldPathSegment(propertyName) };
            RaiseChange(new FieldChange(path, oldValue, value));

            return true;
        }

        /// <summary>
        /// Поднимает локальное изменение (без рекурсии по детям).
        /// </summary>
        protected void RaiseLocalChange(
            string fieldName,
            object? oldValue,
            object? newValue)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(fieldName) };
            RaiseChange(new FieldChange(path, oldValue, newValue));
        }
    }
}