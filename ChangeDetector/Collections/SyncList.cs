using System;
using System.Collections;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector.Collections
{

    /// <summary>
    /// Список с поддержкой трекинга и применения патчей.
    /// 
    /// Особенности:
    /// - генерирует CollectionChange при Add/Remove/Replace/Insert/Clear;
    /// - bubbling изменений от элементов (если они SyncNode) с добавлением индекса в путь;
    /// - может применить входящий патч (Add/Remove/Replace/Move/Clear).
    /// </summary>
    public sealed class SyncList<TItem> : SyncNode, IList<TItem>, ITrackableCollection
    {
        private readonly List<TItem> _inner = new();

        /// <inheritdoc />
        public event Action<CollectionChange>? CollectionChanged;

        /// <summary>
        /// Получение/установка элемента по индексу.
        /// При установке:
        /// - отписывается от старого элемента (если он SyncNode);
        /// - подписывается на новый элемент;
        /// - генерирует патч Replace.
        /// </summary>
        public TItem this[int index]
        {
            get => _inner[index];
            set
            {
                var old = _inner[index];
                UnwireChild(old);

                _inner[index] = value;
                WireChild(value, index);

                CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Replace, index, value));
                RaiseLocalChange($"[{index}]", old, value);
            }
        }

        public int Count => _inner.Count;
        public bool IsReadOnly => false;

        /// <summary>
        /// Добавляет элемент в конец списка.
        /// </summary>
        public void Add(TItem item)
        {
            _inner.Add(item);
            var index = _inner.Count - 1;

            WireChild(item, index);
            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Add, index, item));
            RaiseLocalChange($"[{index}]", null, item);
        }

        /// <summary>
        /// Полностью очищает список.
        /// Отписывает детей и генерирует патч Clear.
        /// </summary>
        public void Clear()
        {
            foreach (var it in _inner)
                UnwireChild(it);

            _inner.Clear();
            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Clear, null, null));
            RaiseLocalChange("Clear", null, null);
        }

        public bool Contains(TItem item) => _inner.Contains(item);
        public void CopyTo(TItem[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);
        public IEnumerator<TItem> GetEnumerator() => _inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int IndexOf(TItem item) => _inner.IndexOf(item);

        /// <summary>
        /// Вставляет элемент по указанному индексу.
        /// </summary>
        public void Insert(int index, TItem item)
        {
            _inner.Insert(index, item);
            WireChild(item, index);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Add, index, item));
            RaiseLocalChange($"[{index}]", null, item);
        }

        /// <summary>
        /// Удаляет первое вхождение элемента.
        /// </summary>
        public bool Remove(TItem item)
        {
            var index = _inner.IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
        }

        /// <summary>
        /// Удаляет элемент по индексу.
        /// </summary>
        public void RemoveAt(int index)
        {
            var old = _inner[index];
            UnwireChild(old);
            _inner.RemoveAt(index);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Remove, index, old));
            RaiseLocalChange($"[{index}]", old, null);
        }

        /// <summary>
        /// Подписывает дочерний элемент (если он SyncNode) на bubbling изменений.
        /// К пути добавляется сегмент вида "[index]".
        /// </summary>
        private void WireChild(TItem item, int index)
        {
            if (item is SyncNode node)
            {
                node.Changed += childChange =>
                {
                    var newPath = new List<FieldPathSegment> { new FieldPathSegment($"[{index}]") };
                    newPath.AddRange(childChange.Path);
                    RaiseChange(new FieldChange(newPath, childChange.OldValue, childChange.NewValue));

                };
            }
        }

        /// <summary>
        /// Отписывает дочерний элемент (если он SyncNode).
        /// Реализацию можно расширить хранением делегатов в словаре,
        /// чтобы отписывать конкретные обработчики.
        /// </summary>
        private void UnwireChild(TItem item)
        {
            if (item is SyncNode node)
            {
                // TODO: при желании хранить и отписывать конкретный делегат
            }
        }

        /// <summary>
        /// Применяет патч к списку (на принимающей стороне).
        /// Используется при получении CollectionChange по сети.
        /// </summary>
        public void ApplyCollectionChange(CollectionChange change)
        {
            switch (change.Kind)
            {
                case CollectionOpKind.Add:
                    {
                        var index = Convert.ToInt32(change.KeyOrIndex);
                        var value = (TItem)ConvertIfNeeded(change.Value, typeof(TItem));
                        Insert(index, value);
                        break;
                    }
                case CollectionOpKind.Remove:
                    {
                        var index = Convert.ToInt32(change.KeyOrIndex);
                        RemoveAt(index);
                        break;
                    }
                case CollectionOpKind.Replace:
                    {
                        var index = Convert.ToInt32(change.KeyOrIndex);
                        var value = (TItem)ConvertIfNeeded(change.Value, typeof(TItem));
                        this[index] = value;
                        break;
                    }
                case CollectionOpKind.Clear:
                    Clear();
                    break;
                case CollectionOpKind.Move:
                    {
                        var (from, to) = (ValueTuple<int, int>)change.KeyOrIndex!;
                        var item = _inner[from];
                        _inner.RemoveAt(from);
                        _inner.Insert(to, item);
                        break;
                    }
            }
        }

        /// <summary>
        /// Простая конвертация значения к требуемому типу.
        /// </summary>
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