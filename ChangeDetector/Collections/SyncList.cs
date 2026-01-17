using System;
using System.Collections;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector.Collections
{
    /// <summary>
    /// Список с поддержкой трекинга, патчей и снапшотов.
    /// </summary>
    public sealed class SyncList<TItem> : SyncNode,
        IList<TItem>,
        ITrackableCollection,
        ISnapshotCollection,
        ISyncIndexableCollection
    {
        private readonly List<TItem> _inner = new();

        // Ключ: дочерний SyncNode, значение: делегат, которым мы подписались.
        private readonly Dictionary<SyncNode, Action<FieldChange>> _childHandlers = new();

        /// <inheritdoc />
        public event Action<CollectionChange>? CollectionChanged;

        public TItem this[int index]
        {
            get => _inner[index];
            set
            {
                var old = _inner[index];
                UnwireChild(old);

                _inner[index] = value;
                WireChild(value);

                CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Replace, index, value));
                RaiseLocalChange($"[{index}]", old, value);
            }
        }

        // ISyncIndexableCollection

        int ISyncIndexableCollection.Count => _inner.Count;

        object? ISyncIndexableCollection.GetElement(string segmentName)
        {
            if (!TryParseIndex(segmentName, out var index))
                throw new InvalidOperationException($"Invalid index segment '{segmentName}' for SyncList.");

            return _inner[index];
        }

        void ISyncIndexableCollection.SetElement(string segmentName, object? value)
        {
            if (!TryParseIndex(segmentName, out var index))
                throw new InvalidOperationException($"Invalid index segment '{segmentName}' for SyncList.");

            var elementType = typeof(TItem);
            var converted = (TItem?)ConvertIfNeeded(value, elementType);
            _inner[index] = converted!;
        }

        // ISnapshotCollection

        void ISnapshotCollection.ApplySnapshotFrom(object? sourceCollection)
        {
            if (sourceCollection is not IEnumerable sourceEnumerable)
                throw new InvalidOperationException();

            foreach (var it in _inner)
                UnwireChild(it);

            _inner.Clear();
            _childHandlers.Clear();

            foreach (var obj in sourceEnumerable)
            {
                var item = (TItem?)ConvertIfNeeded(obj, typeof(TItem));
                _inner.Add(item!);
                WireChild(item!);
            }

            // вся коллекция приведена к снапшоту – уведомляем
            SnapshotApplied?.Invoke();
        }


        public int Count => _inner.Count;
        public bool IsReadOnly => false;

        public void Add(TItem item)
        {
            var index = _inner.Count;
            _inner.Add(item);

            WireChild(item);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Add, index, item));
            RaiseLocalChange($"[{index}]", null, item);
        }

        public void Clear()
        {
            foreach (var it in _inner)
                UnwireChild(it);

            _inner.Clear();
            _childHandlers.Clear();

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Clear, null, null));
            RaiseLocalChange("Clear", null, null);
        }

        public bool Contains(TItem item) => _inner.Contains(item);
        public void CopyTo(TItem[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);
        public IEnumerator<TItem> GetEnumerator() => _inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int IndexOf(TItem item) => _inner.IndexOf(item);

        public void Insert(int index, TItem item)
        {
            _inner.Insert(index, item);
            WireChild(item);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Add, index, item));
            RaiseLocalChange($"[{index}]", null, item);
        }

        public bool Remove(TItem item)
        {
            var index = _inner.IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            var old = _inner[index];
            UnwireChild(old);
            _inner.RemoveAt(index);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Remove, index, old));
            RaiseLocalChange($"[{index}]", old, null);
        }

        /// <summary>
        /// Тихая вставка для входящих патчей (без событий).
        /// </summary>
        private void InsertSilent(int index, TItem item)
        {
            _inner.Insert(index, item);
            WireChild(item);
        }

        /// <summary>
        /// Тихое удаление для входящих патчей (без событий).
        /// </summary>
        private void RemoveAtSilent(int index)
        {
            var old = _inner[index];
            UnwireChild(old);
            _inner.RemoveAt(index);
        }

        /// <summary>
        /// Тихая замена элемента (для входящих патчей).
        /// </summary>
        private void SetItemSilent(int index, TItem value)
        {
            var old = _inner[index];
            UnwireChild(old);
            _inner[index] = value;
            WireChild(value);
        }

        /// <summary>
        /// Подписывает дочерний элемент (если он SyncNode) на bubbling изменений.
        /// Путь строится на основе актуального индекса элемента в списке.
        /// </summary>
        private void WireChild(TItem item)
        {
            if (item is not SyncNode node)
                return;

            // От греха – сначала отписываем, если вдруг уже был.
            UnwireChild(item);

            Action<FieldChange> handler = childChange =>
            {
                // Индекс может меняться, поэтому ищем актуальный.
                var index = _inner.IndexOf(item);
                if (index < 0)
                    return; // элемент уже не в списке, игнорируем.

                var newPath = new List<FieldPathSegment>
                {
                    new FieldPathSegment($"[{index}]")
                };
                newPath.AddRange(childChange.Path);

                RaiseChange(new FieldChange(newPath, childChange.OldValue, childChange.NewValue));
            };

            _childHandlers[node] = handler;
            node.Changed += handler;
        }

        /// <summary>
        /// Отписывает дочерний элемент (если он SyncNode).
        /// </summary>
        private void UnwireChild(TItem item)
        {
            if (item is not SyncNode node)
                return;

            if (_childHandlers.TryGetValue(node, out var handler))
            {
                node.Changed -= handler;
                _childHandlers.Remove(node);
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
                        var value = (TItem)ConvertIfNeeded(change.Value, typeof(TItem))!;
                        InsertSilent(index, value);
                        break;
                    }
                case CollectionOpKind.Remove:
                    {
                        var index = Convert.ToInt32(change.KeyOrIndex);
                        RemoveAtSilent(index);
                        break;
                    }
                case CollectionOpKind.Replace:
                    {
                        var index = Convert.ToInt32(change.KeyOrIndex);
                        var value = (TItem)ConvertIfNeeded(change.Value, typeof(TItem))!;
                        SetItemSilent(index, value);
                        break;
                    }
                case CollectionOpKind.Clear:
                    {
                        foreach (var it in _inner)
                            UnwireChild(it);
                        _inner.Clear();
                        _childHandlers.Clear();
                        break;
                    }
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

        private static bool TryParseIndex(string segmentName, out int index)
        {
            if (!string.IsNullOrEmpty(segmentName) &&
                segmentName[0] == '[' &&
                segmentName[^1] == ']')
            {
                segmentName = segmentName.Substring(1, segmentName.Length - 2);
            }

            return int.TryParse(segmentName, out index);
        }

        private object? ConvertIfNeeded(object? value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            if (targetType.IsInstanceOfType(value))
                return value;
            return Convert.ChangeType(value, targetType);
        }

        // Явная реализация IList<T> для совместимости
        TItem IList<TItem>.this[int index]
        {
            get => this[index];
            set => this[index] = value;
        }
    }
}
