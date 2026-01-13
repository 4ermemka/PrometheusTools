using System;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector.Collections
{

    /// <summary>
    /// Стек с поддержкой трекинга и применения патчей.
    /// 
    /// Основные операции: Push/Pop/Clear.
    /// </summary>
    public sealed class SyncStack<TItem> : SyncNode, ITrackableCollection
    {
        private readonly Stack<TItem> _inner = new();

        /// <inheritdoc />
        public event Action<CollectionChange>? CollectionChanged;

        public int Count => _inner.Count;

        /// <summary>
        /// Кладёт элемент на вершину стека.
        /// </summary>
        public void Push(TItem item)
        {
            _inner.Push(item);
            WireChild(item);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Add, null, item));
            RaiseLocalChange("Push", null, item);
        }

        /// <summary>
        /// Снимает элемент с вершины стека.
        /// </summary>
        public TItem Pop()
        {
            var item = _inner.Pop();
            UnwireChild(item);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Remove, null, item));
            RaiseLocalChange("Pop", item, null);
            return item;
        }

        /// <summary>
        /// Просматривает элемент на вершине стека без удаления.
        /// </summary>
        public TItem Peek() => _inner.Peek();

        private void WireChild(TItem item)
        {
            if (item is SyncNode node)
            {
                node.Changed += childChange =>
                {
                    var newPath = new List<FieldPathSegment> { new FieldPathSegment("[StackItem]") };
                    newPath.AddRange(childChange.Path);
                    RaiseChange(new FieldChange(newPath, childChange.OldValue, childChange.NewValue));
                };
            }
        }

        private void UnwireChild(TItem item)
        {
            if (item is SyncNode node)
            {
                // TODO: отписка при необходимости
            }
        }

        /// <summary>
        /// Применяет патч к стеку.
        /// </summary>
        public void ApplyCollectionChange(CollectionChange change)
        {
            switch (change.Kind)
            {
                case CollectionOpKind.Add:
                    {
                        var value = (TItem)ConvertIfNeeded(change.Value, typeof(TItem));
                        Push(value);
                        break;
                    }
                case CollectionOpKind.Remove:
                    {
                        Pop();
                        break;
                    }
                case CollectionOpKind.Clear:
                    {
                        while (_inner.Count > 0)
                            Pop();
                        break;
                    }
                default:
                    throw new NotSupportedException("Replace/Move для стека лучше слать как полный снапшот.");
            }
        }

        private object? ConvertIfNeeded(object? value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            if (targetType.IsInstanceOfType(value))
                return value;
            return Convert.ChangeType(value, targetType);
        }
    }

}