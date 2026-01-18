using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Shared.ChangeDetector.Collections
{
    /// <summary>
    /// Список с поддержкой трекинга, патчей и снапшотов.
    /// </summary>
    public sealed class SyncList<T> : SyncNode,
        IList<T>,
        ISnapshotCollection,
        ISyncIndexableCollection
    {
        private readonly List<T> _items = new();
        private readonly IndexTracker<T> _indexTracker;

        // Для элементов SyncNode храним подписки
        private readonly Dictionary<SyncNode, Action<FieldChange>> _nodeHandlers = new();

        // SyncProperty для отслеживания размера (опционально)
        [SyncField(TrackChanges = false)]
        private SyncProperty<int> ItemCount { get; set; } = null!;

        public event Action<CollectionChange>? CollectionChanged;

        public SyncList(IEqualityComparer<T>? comparer = null)
        {
            _indexTracker = new IndexTracker<T>(comparer);
            InitializeSyncProperties();
            UpdateItemCount();
        }

        #region IList<T> Implementation

        public T this[int index]
        {
            get => _items[index];
            set
            {
                var oldItem = _items[index];
                if (EqualityComparer<T>.Default.Equals(oldItem, value))
                    return;

                UnwireChild(oldItem);
                _items[index] = value;
                WireChild(value, index);

                // Обновляем индексный трекер
                _indexTracker.Remove(oldItem, index);
                _indexTracker.Add(value, index);

                // Генерируем патч
                GenerateReplacePatch(index, oldItem, value);

                CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Replace, index, value));
            }
        }

        public int Count => _items.Count;
        public bool IsReadOnly => false;

        public void Add(T item)
        {
            int index = _items.Count;
            _items.Add(item);
            _indexTracker.Add(item, index);

            WireChild(item, index);
            UpdateItemCount();

            GenerateAddPatch(index, item);
            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Add, index, item));
        }

        public void Clear()
        {
            foreach (var item in _items)
            {
                UnwireChild(item);
            }

            _items.Clear();
            _indexTracker.Clear();
            _nodeHandlers.Clear();
            UpdateItemCount();

            GenerateClearPatch();
            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Clear, null, null));
        }

        public bool Contains(T item) => _indexTracker.GetCount(item) > 0;

        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int IndexOf(T item)
        {
            return _indexTracker.TryGetFirstIndex(item, out var index) ? index : -1;
        }

        public void Insert(int index, T item)
        {
            _items.Insert(index, item);

            // Сдвигаем индексы для элементов после вставки
            _indexTracker.ShiftIndices(index, 1);
            _indexTracker.Add(item, index);

            // Перенумеровываем подписки
            RenumberSubscriptionsFrom(index, 1);

            WireChild(item, index);
            UpdateItemCount();

            GenerateInsertPatch(index, item);
            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Add, index, item));
        }

        public bool Remove(T item)
        {
            if (_indexTracker.TryGetFirstIndex(item, out var index))
            {
                RemoveAt(index);
                return true;
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            var item = _items[index];
            _items.RemoveAt(index);

            UnwireChild(item);
            _indexTracker.Remove(item, index);

            // Сдвигаем индексы для элементов после удаления
            _indexTracker.ShiftIndices(index + 1, -1);

            // Перенумеровываем подписки
            RenumberSubscriptionsFrom(index + 1, -1);

            UpdateItemCount();

            GenerateRemovePatch(index, item);
            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Remove, index, item));
        }

        #endregion

        #region ISyncIndexableCollection Implementation

        int ISyncIndexableCollection.Count => _items.Count;

        object? ISyncIndexableCollection.GetElement(string segmentName)
        {
            if (!PathHelper.IsCollectionIndex(segmentName))
                throw new InvalidOperationException($"Invalid index segment '{segmentName}' for SyncList.");

            var index = PathHelper.ParseListIndex(segmentName);
            if (index < 0 || index >= _items.Count)
                throw new IndexOutOfRangeException($"Index {index} is out of range [0, {_items.Count}).");

            return _items[index];
        }

        void ISyncIndexableCollection.SetElement(string segmentName, object? value)
        {
            if (!PathHelper.IsCollectionIndex(segmentName))
                throw new InvalidOperationException($"Invalid index segment '{segmentName}' for SyncList.");

            var index = PathHelper.ParseListIndex(segmentName);
            var converted = (T?)ConvertIfNeeded(value, typeof(T));

            if (index == _items.Count)
            {
                // Добавление в конец
                Add(converted!);
            }
            else if (index >= 0 && index < _items.Count)
            {
                // Замена существующего
                this[index] = converted!;
            }
            else
            {
                throw new IndexOutOfRangeException($"Index {index} is out of range [0, {_items.Count}].");
            }
        }

        #endregion

        #region ISnapshotCollection Implementation

        void ISnapshotCollection.ApplySnapshotFrom(object? sourceCollection)
        {
            if (sourceCollection is not IEnumerable<T> source)
                throw new InvalidOperationException($"Source must be IEnumerable<{typeof(T).Name}>.");

            // Отписываемся от старых элементов
            foreach (var item in _items)
            {
                UnwireChild(item);
            }

            // Полностью заменяем коллекцию
            _items.Clear();
            _indexTracker.Clear();
            _nodeHandlers.Clear();

            // Добавляем новые элементы
            int index = 0;
            foreach (var item in source)
            {
                _items.Add(item);
                _indexTracker.Add(item, index);
                WireChild(item, index);
                index++;
            }

            UpdateItemCount();
            SnapshotApplied?.Invoke();
        }

        #endregion

        #region Child Wiring

        private void WireChild(T item, int index)
        {
            if (item is SyncNode node)
            {
                // Отписываемся если уже подписаны
                if (_nodeHandlers.ContainsKey(node))
                {
                    UnwireChild(item);
                }

                Action<FieldChange> handler = change =>
                {
                    // Строим полный путь: [index] + внутренний путь
                    var path = new List<FieldPathSegment>
                    {
                        new FieldPathSegment($"[{index}]")
                    };
                    path.AddRange(change.Path);

                    RaiseChange(new FieldChange(path, change.OldValue, change.NewValue));
                };

                _nodeHandlers[node] = handler;
                node.Changed += handler;
            }
        }

        private void UnwireChild(T item)
        {
            if (item is SyncNode node && _nodeHandlers.TryGetValue(node, out var handler))
            {
                node.Changed -= handler;
                _nodeHandlers.Remove(node);
            }
        }

        private void RenumberSubscriptionsFrom(int fromIndex, int delta)
        {
            if (delta == 0) return;

            // Обновляем подписки для элементов SyncNode
            foreach (var kvp in _nodeHandlers.ToList())
            {
                var node = kvp.Key;

                // Ищем новый индекс элемента
                for (int i = 0; i < _items.Count; i++)
                {
                    if (ReferenceEquals(_items[i], node))
                    {
                        // Нашли элемент, нужно обновить обработчик
                        if (i >= fromIndex)
                        {
                            // Создаем новый обработчик с правильным индексом
                            var oldHandler = kvp.Value;
                            node.Changed -= oldHandler;

                            Action<FieldChange> newHandler = change =>
                            {
                                var path = new List<FieldPathSegment>
                                {
                                    new FieldPathSegment($"[{i}]")
                                };
                                path.AddRange(change.Path);

                                RaiseChange(new FieldChange(path, change.OldValue, change.NewValue));
                            };

                            _nodeHandlers[node] = newHandler;
                            node.Changed += newHandler;
                        }
                        break;
                    }
                }
            }
        }

        #endregion

        #region Patch Generation

        private void GenerateAddPatch(int index, T item)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment($"[{index}]") };
            RaiseChange(new FieldChange(path, null, item));
        }

        private void GenerateRemovePatch(int index, T item)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment($"[{index}]") };
            RaiseChange(new FieldChange(path, item, null));
        }

        private void GenerateReplacePatch(int index, T oldItem, T newItem)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment($"[{index}]") };
            RaiseChange(new FieldChange(path, oldItem, newItem));
        }

        private void GenerateInsertPatch(int index, T item)
        {
            // Вставка - это частный случай добавления
            GenerateAddPatch(index, item);
        }

        private void GenerateClearPatch()
        {
            // Для очистки генерируем специальный патч
            var path = new List<FieldPathSegment> { new FieldPathSegment("Clear") };
            RaiseChange(new FieldChange(path, null, null));
        }

        #endregion

        #region Helpers

        private void UpdateItemCount()
        {
            if (ItemCount != null)
            {
                ItemCount.Value = _items.Count;
            }
        }

        private static bool TryParseIndex(string segmentName, out int index)
        {
            if (PathHelper.IsCollectionIndex(segmentName))
            {
                try
                {
                    index = PathHelper.ParseListIndex(segmentName);
                    return true;
                }
                catch
                {
                    index = -1;
                    return false;
                }
            }

            index = -1;
            return false;
        }

        #endregion

        #region ApplyCollectionChange (для обратной совместимости)

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
                        var value = (T)ConvertIfNeeded(change.Value, typeof(T))!;
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
                        var value = (T)ConvertIfNeeded(change.Value, typeof(T))!;
                        SetItemSilent(index, value);
                        break;
                    }
                case CollectionOpKind.Clear:
                    {
                        ClearSilent();
                        break;
                    }
                case CollectionOpKind.Move:
                    {
                        var (from, to) = (ValueTuple<int, int>)change.KeyOrIndex!;
                        var item = _items[from];
                        _items.RemoveAt(from);
                        _items.Insert(to, item);
                        RenumberSubscriptionsAfterMove(from, to);
                        break;
                    }
            }
        }

        private void InsertSilent(int index, T item)
        {
            _items.Insert(index, item);
            _indexTracker.ShiftIndices(index, 1);
            _indexTracker.Add(item, index);
            RenumberSubscriptionsFrom(index, 1);
            WireChild(item, index);
            UpdateItemCount();
        }

        private void RemoveAtSilent(int index)
        {
            var item = _items[index];
            _items.RemoveAt(index);
            UnwireChild(item);
            _indexTracker.Remove(item, index);
            _indexTracker.ShiftIndices(index + 1, -1);
            RenumberSubscriptionsFrom(index + 1, -1);
            UpdateItemCount();
        }

        private void SetItemSilent(int index, T value)
        {
            var oldItem = _items[index];
            UnwireChild(oldItem);
            _items[index] = value;
            _indexTracker.Remove(oldItem, index);
            _indexTracker.Add(value, index);
            WireChild(value, index);
        }

        private void ClearSilent()
        {
            foreach (var item in _items)
            {
                UnwireChild(item);
            }
            _items.Clear();
            _indexTracker.Clear();
            _nodeHandlers.Clear();
            UpdateItemCount();
        }

        private void RenumberSubscriptionsAfterMove(int fromIndex, int toIndex)
        {
            // При перемещении нужно обновить все подписки между fromIndex и toIndex
            int start = Math.Min(fromIndex, toIndex);
            int end = Math.Max(fromIndex, toIndex);
            int delta = fromIndex < toIndex ? -1 : 1;

            for (int i = start; i <= end; i++)
            {
                if (_items[i] is SyncNode node && _nodeHandlers.TryGetValue(node, out var handler))
                {
                    // Обновляем обработчик с новым индексом
                    node.Changed -= handler;

                    Action<FieldChange> newHandler = change =>
                    {
                        var path = new List<FieldPathSegment>
                        {
                            new FieldPathSegment($"[{i}]")
                        };
                        path.AddRange(change.Path);

                        RaiseChange(new FieldChange(path, change.OldValue, change.NewValue));
                    };

                    _nodeHandlers[node] = newHandler;
                    node.Changed += newHandler;
                }
            }
        }

        #endregion
    }
}