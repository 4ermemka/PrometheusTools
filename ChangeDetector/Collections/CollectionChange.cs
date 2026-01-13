using System.Collections;
using UnityEngine;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Описание изменения коллекции (патч).
    /// </summary>
    public sealed class CollectionChange
    {
        /// <summary>
        /// Тип операции (добавление, удаление, замена, перемещение, очистка).
        /// </summary>
        public CollectionOpKind Kind { get; }

        /// <summary>
        /// Индекс, ключ или другая идентификация элемента (в зависимости от коллекции).
        /// </summary>
        public object? KeyOrIndex { get; }

        /// <summary>
        /// Добавляемое/новое значение (если требуется для операции).
        /// </summary>
        public object? Value { get; }

        public CollectionChange(CollectionOpKind kind, object? keyOrIndex, object? value)
        {
            Kind = kind;
            KeyOrIndex = keyOrIndex;
            Value = value;
        }
    }
}