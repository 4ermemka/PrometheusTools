using System;
using System.Collections;
using System.Collections.Generic;

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
        private readonly Dictionary<T, List<int>> _indexMap = new();
        private readonly Dictionary<SyncNode, Action<FieldChange>> _nodeHandlers = new();

        /// <summary>
        /// Событие изменения коллекции (добавление, удаление, замена)
        /// </summary>
        public event Action<CollectionChange>? CollectionChanged;

        #region IList<T> Implementation

        public T this[int index]
        {
            get => _items[index];
            set
            {
                if (index < 0 || index >= _items.Count)
                    throw new IndexOutOfRangeException();

                var oldItem = _items[index];
                if (EqualityComparer<T>.Default.Equals(oldItem, value))
                    return;

                // Обновляем индексный мап
                RemoveFromIndexMap(oldItem, index);
                AddToIndexMap(value, index);

                // Отписываемся от старого элемента, подписываемся на новый
                UnwireChild(oldItem);
                _items[index] = value;
                WireChild(value, index);

                // Генерируем патч
                GenerateReplacePatch(index, oldItem, value);

                // Генерируем событие коллекции
                CollectionChanged?.Invoke(new CollectionChange(
                    CollectionOpKind.Replace,
                    index,
                    value,
                    oldItem
                ));
            }
        }

        public int Count => _items.Count;
        public bool IsReadOnly => false;

        public void Add(T item)
        {
            int index = _items.Count;
            _items.Add(item);
            AddToIndexMap(item, index);

            WireChild(item, index);

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
            _indexMap.Clear();
            _nodeHandlers.Clear();

            GenerateClearPatch();
            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Clear, null, null));
        }

        public bool Contains(T item)
        {
            return _indexMap.ContainsKey(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _items.CopyTo(array, arrayIndex);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int IndexOf(T item)
        {
            if (_indexMap.TryGetValue(item, out var indices) && indices.Count > 0)
            {
                return indices[0];
            }
            return -1;
        }

        public void Insert(int index, T item)
        {
            if (index < 0 || index > _items.Count)
                throw new IndexOutOfRangeException();

            _items.Insert(index, item);

            // Сдвигаем индексы для элементов после вставки
            ShiftIndices(index, 1);
            AddToIndexMap(item, index);

            // Обновляем подписки
            RenumberSubscriptionsFrom(index, 1);

            WireChild(item, index);

            GenerateInsertPatch(index, item);
            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Add, index, item));
        }

        public bool Remove(T item)
        {
            if (_indexMap.TryGetValue(item, out var indices) && indices.Count > 0)
            {
                RemoveAt(indices[0]);
                return true;
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _items.Count)
                throw new IndexOutOfRangeException();

            var item = _items[index];
            _items.RemoveAt(index);

            UnwireChild(item);
            RemoveFromIndexMap(item, index);

            // Сдвигаем индексы для элементов после удаления
            ShiftIndices(index + 1, -1);

            // Обновляем подписки
            RenumberSubscriptionsFrom(index + 1, -1);

            GenerateRemovePatch(index, item);
            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Remove, index, null, item));
        }

        #endregion

        #region ISyncIndexableCollection Implementation

        object? ISyncIndexableCollection.GetElement(string segmentName)
        {
            if (!int.TryParse(segmentName, out var index))
                throw new InvalidOperationException($"Invalid index segment '{segmentName}' for SyncList.");

            if (index < 0 || index >= _items.Count)
                throw new IndexOutOfRangeException($"Index {index} is out of range [0, {_items.Count}).");

            return _items[index];
        }

        void ISyncIndexableCollection.SetElement(string segmentName, object? value)
        {
            if (!int.TryParse(segmentName, out var index))
                throw new InvalidOperationException($"Invalid index segment '{segmentName}' for SyncList.");

            var converted = (T?)value;

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

            // 1. Отписываемся от старых элементов
            foreach (var item in _items)
            {
                UnwireChild(item);
            }

            // 2. Полностью очищаем коллекцию
            _items.Clear();
            _indexMap.Clear();
            _nodeHandlers.Clear();

            // 3. Добавляем новые элементы (тихо, без генерации патчей)
            int index = 0;
            foreach (var item in source)
            {
                _items.Add(item);
                AddToIndexMap(item, index);
                WireChild(item, index); // Подписываемся на новые элементы
                index++;
            }

            // 4. Уведомляем о применении снапшота
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
                        new FieldPathSegment(index.ToString())
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

            // Нужно найти все SyncNode элементы и обновить их обработчики
            for (int i = Math.Max(0, fromIndex); i < _items.Count; i++)
            {
                if (_items[i] is SyncNode node && _nodeHandlers.TryGetValue(node, out var oldHandler))
                {
                    // Создаем новый обработчик с правильным индексом
                    node.Changed -= oldHandler;

                    int currentIndex = i; // Захватываем текущий индекс
                    Action<FieldChange> newHandler = change =>
                    {
                        var path = new List<FieldPathSegment>
                        {
                            new FieldPathSegment(currentIndex.ToString())
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

        #region Index Map Management

        private void AddToIndexMap(T item, int index)
        {
            if (!_indexMap.TryGetValue(item, out var indices))
            {
                indices = new List<int>();
                _indexMap[item] = indices;
            }

            // Вставляем с сохранением порядка
            var insertPos = indices.BinarySearch(index);
            if (insertPos < 0) insertPos = ~insertPos;
            indices.Insert(insertPos, index);
        }

        private void RemoveFromIndexMap(T item, int index)
        {
            if (_indexMap.TryGetValue(item, out var indices))
            {
                indices.Remove(index);
                if (indices.Count == 0)
                {
                    _indexMap.Remove(item);
                }
            }
        }

        private void ShiftIndices(int fromIndex, int delta)
        {
            foreach (var indices in _indexMap.Values)
            {
                for (int i = 0; i < indices.Count; i++)
                {
                    if (indices[i] >= fromIndex)
                    {
                        indices[i] += delta;
                    }
                }
            }
        }

        #endregion

        #region Patch Generation

        private void GenerateAddPatch(int index, T item)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(index.ToString()) };
            RaiseChange(new FieldChange(path, null, item));
        }

        private void GenerateRemovePatch(int index, T item)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(index.ToString()) };
            RaiseChange(new FieldChange(path, item, null));
        }

        private void GenerateReplacePatch(int index, T oldItem, T newItem)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(index.ToString()) };
            RaiseChange(new FieldChange(path, oldItem, newItem));
        }

        private void GenerateInsertPatch(int index, T item)
        {
            GenerateAddPatch(index, item);
        }

        private void GenerateClearPatch()
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment("Clear") };
            RaiseChange(new FieldChange(path, null, null));
        }

        #endregion

        #region ApplyCollectionChange (для обратной совместимости)

        /// <summary>
        /// Применяет патч к списку (на принимающей стороне).
        /// </summary>
        public void ApplyCollectionChange(CollectionChange change)
        {
            switch (change.Kind)
            {
                case CollectionOpKind.Add:
                    {
                        var index = Convert.ToInt32(change.KeyOrIndex);
                        var value = (T)change.Value!;
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
                        var value = (T)change.Value!;
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
                        UpdateIndexMapAfterMove(from, to);
                        RenumberSubscriptionsAfterMove(from, to);
                        break;
                    }
            }
        }

        private void InsertSilent(int index, T item)
        {
            _items.Insert(index, item);
            ShiftIndices(index, 1);
            AddToIndexMap(item, index);
            RenumberSubscriptionsFrom(index, 1);
            WireChild(item, index);
        }

        private void RemoveAtSilent(int index)
        {
            var item = _items[index];
            _items.RemoveAt(index);
            UnwireChild(item);
            RemoveFromIndexMap(item, index);
            ShiftIndices(index + 1, -1);
            RenumberSubscriptionsFrom(index + 1, -1);
        }

        private void SetItemSilent(int index, T value)
        {
            var oldItem = _items[index];
            UnwireChild(oldItem);
            _items[index] = value;
            RemoveFromIndexMap(oldItem, index);
            AddToIndexMap(value, index);
            WireChild(value, index);
        }

        private void ClearSilent()
        {
            foreach (var item in _items)
            {
                UnwireChild(item);
            }
            _items.Clear();
            _indexMap.Clear();
            _nodeHandlers.Clear();
        }

        private void UpdateIndexMapAfterMove(int fromIndex, int toIndex)
        {
            // Обновляем индексный мап после перемещения
            var item = _items[toIndex]; // item теперь на новой позиции

            if (_indexMap.TryGetValue(item, out var indices))
            {
                indices.Remove(fromIndex);

                var insertPos = indices.BinarySearch(toIndex);
                if (insertPos < 0) insertPos = ~insertPos;
                indices.Insert(insertPos, toIndex);
            }

            // Обновляем индексы элементов между fromIndex и toIndex
            int start = Math.Min(fromIndex, toIndex);
            int end = Math.Max(fromIndex, toIndex);
            int direction = fromIndex < toIndex ? -1 : 1;

            for (int i = start; i <= end; i++)
            {
                if (i == toIndex) continue;

                var currentItem = _items[i];
                if (_indexMap.TryGetValue(currentItem, out var currentIndices))
                {
                    for (int j = 0; j < currentIndices.Count; j++)
                    {
                        if (currentIndices[j] == i)
                        {
                            currentIndices[j] += direction;
                            break;
                        }
                    }
                }
            }
        }

        private void RenumberSubscriptionsAfterMove(int fromIndex, int toIndex)
        {
            int start = Math.Min(fromIndex, toIndex);
            int end = Math.Max(fromIndex, toIndex);

            for (int i = start; i <= end; i++)
            {
                if (_items[i] is SyncNode node && _nodeHandlers.TryGetValue(node, out var handler))
                {
                    // Обновляем обработчик с новым индексом
                    node.Changed -= handler;

                    int currentIndex = i; // Захватываем текущий индекс
                    Action<FieldChange> newHandler = change =>
                    {
                        var path = new List<FieldPathSegment>
                        {
                            new FieldPathSegment(currentIndex.ToString())
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

        #region Serialization Support

        /// <summary>
        /// Получает данные для сериализации (просто список элементов)
        /// </summary>
        public new List<T> GetSerializableData()
        {
            return new List<T>(_items);
        }

        /// <summary>
        /// Восстанавливает данные из сериализованного списка
        /// </summary>
        public void ApplySerializedData(List<T> data)
        {
            // Отписываемся от старых элементов
            foreach (var item in _items)
            {
                UnwireChild(item);
            }

            // Очищаем и заполняем новыми элементами
            _items.Clear();
            _indexMap.Clear();
            _nodeHandlers.Clear();

            for (int i = 0; i < data.Count; i++)
            {
                _items.Add(data[i]);
                AddToIndexMap(data[i], i);
                WireChild(data[i], i);
            }

            SnapshotApplied?.Invoke();
        }

        #endregion
    }

    /// <summary>
    /// Изменение в коллекции
    /// </summary>
    public class CollectionChange
    {
        public CollectionOpKind Kind { get; }
        public object? KeyOrIndex { get; }
        public object? Value { get; }
        public object? OldValue { get; }

        public CollectionChange(CollectionOpKind kind, object? keyOrIndex, object? value, object? oldValue = null)
        {
            Kind = kind;
            KeyOrIndex = keyOrIndex;
            Value = value;
            OldValue = oldValue;
        }
    }

    /// <summary>
    /// Тип операции в коллекции
    /// </summary>
    public enum CollectionOpKind
    {
        Add,
        Remove,
        Replace,
        Clear,
        Move
    }
}