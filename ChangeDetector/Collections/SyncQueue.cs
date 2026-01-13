using System;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector.Collections
{

    /// <summary>
    /// Очередь с поддержкой трекинга и применения патчей.
    /// 
    /// Основные операции: Enqueue/Dequeue/Clear.
    /// Для сложных операций (Reorder) лучше слать полный снапшот.
    /// </summary>
    public sealed class SyncQueue<TItem> : SyncNode, ITrackableCollection
    {
        private readonly Queue<TItem> _inner = new();

        /// <inheritdoc />
        public event Action<CollectionChange>? CollectionChanged;

        public int Count => _inner.Count;

        /// <summary>
        /// Добавляет элемент в конец очереди.
        /// </summary>
        public void Enqueue(TItem item)
        {
            _inner.Enqueue(item);
            WireChild(item);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Add, null, item));
            RaiseLocalChange("Enqueue", null, item);
        }

        /// <summary>
        /// Извлекает элемент из начала очереди.
        /// </summary>
        public TItem Dequeue()
        {
            var item = _inner.Dequeue();
            UnwireChild(item);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Remove, null, item));
            RaiseLocalChange("Dequeue", item, null);
            return item;
        }

        /// <summary>
        /// Просматривает элемент в начале очереди без удаления.
        /// </summary>
        public TItem Peek() => _inner.Peek();

        /// <summary>
        /// Подписывает элемент (если он SyncNode) на bubbling изменений.
        /// Индекс в очереди здесь не фиксируем, путь будет условным ("[QueueItem]").
        /// </summary>
        private void WireChild(TItem item)
        {
            if (item is SyncNode node)
            {
                node.Changed += childChange =>
                {
                    var newPath = new List<FieldPathSegment> { new FieldPathSegment("[QueueItem]") };
                    newPath.AddRange(childChange.Path);
                    RaiseChange(new FieldChange(newPath, childChange.OldValue, childChange.NewValue));

                };
            }
        }

        /// <summary>
        /// Отписка элемента из очереди (если он SyncNode).
        /// </summary>
        private void UnwireChild(TItem item)
        {
            if (item is SyncNode node)
            {
                // TODO: при необходимости хранить и отписывать конкретный делегат
            }
        }

        /// <summary>
        /// Применяет патч к очереди.
        /// </summary>
        public void ApplyCollectionChange(CollectionChange change)
        {
            switch (change.Kind)
            {
                case CollectionOpKind.Add:
                    {
                        var value = (TItem)ConvertIfNeeded(change.Value, typeof(TItem));
                        Enqueue(value);
                        break;
                    }
                case CollectionOpKind.Remove:
                    {
                        Dequeue();
                        break;
                    }
                case CollectionOpKind.Clear:
                    {
                        while (_inner.Count > 0)
                            Dequeue();
                        break;
                    }
                default:
                    throw new NotSupportedException("Replace/Move для очереди лучше слать как полный снапшот.");
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