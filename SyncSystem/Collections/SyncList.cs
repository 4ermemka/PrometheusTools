using Assets.Scripts.Network.NetCore;
using Assets.Shared.SyncSystem.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Assets.Shared.SyncSystem.Collections
{
    public class SyncList<T> : TrackableNode, IList<T>
    {
        private readonly List<T> _items = new List<T>();
        private readonly Dictionary<T, Action<string, object, object>> _elementChangeHandlers = new Dictionary<T, Action<string, object, object>>();

        #region IList<T> Implementation

        public T this[int index]
        {
            get => _items[index];
            set
            {
                var oldValue = _items[index];
                if (!Equals(oldValue, value))
                {
                    UnsubscribeFromElement(oldValue);
                    _items[index] = value;
                    SubscribeToElement(value, index);

                    // Генерируем патч для замены элемента
                    OnChanged($"replace/{index}", oldValue, value);
                }
            }
        }

        public int Count => _items.Count;
        public bool IsReadOnly => false;

        public void Add(T item)
        {
            int index = _items.Count;
            _items.Add(item);
            SubscribeToElement(item, index);
            OnChanged($"add/{index}", null, item);
        }

        public void Insert(int index, T item)
        {
            _items.Insert(index, item);
            ReindexElementsFrom(index + 1);
            SubscribeToElement(item, index);
            OnChanged($"insert/{index}", null, item);
        }

        public bool Remove(T item)
        {
            int index = _items.IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            var oldValue = _items[index];
            UnsubscribeFromElement(oldValue);
            _items.RemoveAt(index);
            ReindexElementsFrom(index);
            OnChanged($"remove/{index}", oldValue, null);
        }

        public void Clear()
        {
            var oldItems = _items.ToList();

            foreach (var item in _items)
            {
                UnsubscribeFromElement(item);
            }

            _items.Clear();
            _elementChangeHandlers.Clear();

            OnChanged("clear", oldItems, null);
        }

        public bool Contains(T item) => _items.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public int IndexOf(T item) => _items.IndexOf(item);

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        #endregion

        #region Element Tracking

        private void SubscribeToElement(T element, int index)
        {
            if (element == null) return;

            if (element is ITrackable trackable)
            {
                Action<string, object, object> handler = (path, oldVal, newVal) =>
                {
                    string elementPath = $"[{index}]";
                    if (!string.IsNullOrEmpty(path))
                    {
                        elementPath += $".{path}";
                    }

                    // Пробрасываем изменение элемента наверх
                    OnChanged(elementPath, oldVal, newVal);
                };

                trackable.Changed += handler;
                _elementChangeHandlers[element] = handler;
            }
        }

        private void UnsubscribeFromElement(T element)
        {
            if (element == null || !_elementChangeHandlers.TryGetValue(element, out var handler))
                return;

            if (element is ITrackable trackable)
            {
                trackable.Changed -= handler;
            }

            _elementChangeHandlers.Remove(element);
        }

        private void ReindexElementsFrom(int startIndex)
        {
            for (int i = startIndex; i < _items.Count; i++)
            {
                var element = _items[i];
                if (element != null && _elementChangeHandlers.TryGetValue(element, out var oldHandler))
                {
                    if (element is ITrackable trackable)
                    {
                        trackable.Changed -= oldHandler;
                    }
                    SubscribeToElement(element, i);
                }
            }
        }

        #endregion

        #region Переопределение ApplyPatch для обработки специфичных путей

        public override void ApplyPatch(string path, object value)
        {
            if (string.IsNullOrEmpty(path))
            {
                // Снапшот всего списка
                ApplySnapshot(value);
                return;
            }

            // Разбираем путь на части
            var dotIndex = path.IndexOf('.');

            if (dotIndex >= 0)
            {
                // Путь содержит точку: [index].property или operation.property
                var firstPart = path.Substring(0, dotIndex);
                var remainingPath = path.Substring(dotIndex + 1);

                if (firstPart.StartsWith("[") && firstPart.EndsWith("]"))
                {
                    // Обращение к элементу списка: [index].property
                    ProcessElementPropertyPath(firstPart, remainingPath, value);
                }
                else
                {
                    // Неожиданный формат, передаем базовому классу
                    base.ApplyPatch(path, value);
                }
            }
            else
            {
                // Путь без точек - операция над списком или индекс
                ProcessListOperation(path, value);
            }
        }

        private void ProcessElementPropertyPath(string indexPart, string propertyPath, object value)
        {
            // Извлекаем индекс из строки вида "[index]"
            var indexStr = indexPart.Substring(1, indexPart.Length - 2);
            if (int.TryParse(indexStr, out int index) && index >= 0 && index < _items.Count)
            {
                var element = _items[index];
                if (element is ITrackable trackable)
                {
                    trackable.ApplyPatch(propertyPath, value);
                }
            }
        }

        private void ProcessListOperation(string operationPath, object value)
        {
            // Если путь начинается с "[" - обращение к элементу
            if (operationPath.StartsWith("[") && operationPath.Contains("]"))
            {
                int bracketIndex = operationPath.IndexOf(']');
                string indexStr = operationPath.Substring(1, bracketIndex - 1);

                if (int.TryParse(indexStr, out int index) && index >= 0 && index < _items.Count)
                {
                    if (bracketIndex == operationPath.Length - 1)
                    {
                        // [index] - замена элемента
                        ApplyReplaceOperation(index, value);
                    }
                    else if (operationPath[bracketIndex + 1] == '/')
                    {
                        // [index]/operation - операция над элементом (нестандартный случай)
                        var operation = operationPath.Substring(bracketIndex + 2);
                        ApplyElementOperation(index, operation, value);
                    }
                }
            }
            else if (operationPath.Contains("/"))
            {
                // Операция над списком
                ApplyCollectionOperation(operationPath, value);
            }
            else
            {
                // Простой путь - передаем базовому классу
                base.ApplyPatch(operationPath, value);
            }
        }

        private void ApplyCollectionOperation(string operationPath, object value)
        {
            var parts = operationPath.Split('/');
            string operation = parts[0];

            switch (operation)
            {
                case "add":
                    ApplyAddOperation(parts, value);
                    break;

                case "insert":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int insertIndex))
                    {
                        ApplyInsertOperation(insertIndex, value);
                    }
                    break;

                case "remove":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int removeIndex))
                    {
                        ApplyRemoveOperation(removeIndex, value);
                    }
                    break;

                case "replace":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int replaceIndex))
                    {
                        ApplyReplaceOperation(replaceIndex, value);
                    }
                    break;

                case "move":
                    if (parts.Length > 2 &&
                        int.TryParse(parts[1], out int fromIndex) &&
                        int.TryParse(parts[2], out int toIndex))
                    {
                        ApplyMoveOperation(fromIndex, toIndex);
                    }
                    break;

                case "clear":
                    ApplyClearOperation();
                    break;
            }
        }

        private void ApplyElementOperation(int index, string operation, object value)
        {
            // Операции над конкретным элементом
            switch (operation)
            {
                case "replace":
                    ApplyReplaceOperation(index, value);
                    break;
            }
        }

        #endregion

        #region Применение операций (вызываются из сети)

        private void ApplyAddOperation(string[] parts, object value)
        {
            int index = _items.Count;
            if (parts.Length > 1 && int.TryParse(parts[1], out int specifiedIndex))
            {
                index = specifiedIndex;
            }

            T item = ConvertValue<T>(value);
            _items.Insert(index, item);
            ReindexElementsFrom(index + 1);
            SubscribeToElement(item, index);

            // Уведомляем о применении патча из сети
            OnPatched($"add/{index}", item);
        }

        private void ApplyInsertOperation(int index, object value)
        {
            T item = ConvertValue<T>(value);
            _items.Insert(index, item);
            ReindexElementsFrom(index + 1);
            SubscribeToElement(item, index);

            OnPatched($"insert/{index}", item);
        }

        private void ApplyRemoveOperation(int index, object value)
        {
            if (index >= 0 && index < _items.Count)
            {
                var oldItem = _items[index];
                UnsubscribeFromElement(oldItem);
                _items.RemoveAt(index);
                ReindexElementsFrom(index);

                OnPatched($"remove/{index}", oldItem);
            }
        }

        private void ApplyReplaceOperation(int index, object value)
        {
            if (index >= 0 && index < _items.Count)
            {
                var oldItem = _items[index];
                UnsubscribeFromElement(oldItem);

                T newItem = ConvertValue<T>(value);
                _items[index] = newItem;
                SubscribeToElement(newItem, index);

                OnPatched($"[{index}]", newItem);
            }
        }

        private void ApplyMoveOperation(int fromIndex, int toIndex)
        {
            if (fromIndex >= 0 && fromIndex < _items.Count &&
                toIndex >= 0 && toIndex < _items.Count && fromIndex != toIndex)
            {
                var item = _items[fromIndex];
                _items.RemoveAt(fromIndex);

                int adjustedToIndex = toIndex;
                if (fromIndex < toIndex)
                {
                    adjustedToIndex--;
                }

                _items.Insert(adjustedToIndex, item);

                // Переподписываем затронутые элементы
                int start = Math.Min(fromIndex, adjustedToIndex);
                int end = Math.Max(fromIndex, adjustedToIndex);
                for (int i = start; i <= end; i++)
                {
                    var elem = _items[i];
                    if (elem != null && _elementChangeHandlers.TryGetValue(elem, out var oldHandler))
                    {
                        if (elem is ITrackable trackable)
                        {
                            trackable.Changed -= oldHandler;
                        }
                        SubscribeToElement(elem, i);
                    }
                }

                OnPatched($"move/{fromIndex}/{toIndex}", item);
            }
        }

        private void ApplyClearOperation()
        {
            var oldItems = _items.ToList();

            foreach (var item in _items)
            {
                UnsubscribeFromElement(item);
            }

            _items.Clear();
            _elementChangeHandlers.Clear();

            OnPatched("clear", oldItems);
        }

        private TValue ConvertValue<TValue>(object value)
        {
            return JsonGameSerializer.ConvertValue<TValue>(value);
        }

        #endregion

        #region Снапшоты

        public override Dictionary<string, object> CreateSnapshot()
        {
            var snapshot = new Dictionary<string, object>();
            var items = new List<object>();

            foreach (var item in _items)
            {
                if (item is ITrackable trackable)
                {
                    // Для ITrackable объектов используем их собственные снапшоты
                    if (trackable is TrackableNode node)
                    {
                        items.Add(node.CreateSnapshot());
                    }
                    else
                    {
                        // Для простых ITrackable сериализуем в JSON
                        items.Add(JsonGameSerializer.Serialize(item));
                    }
                }
                else
                {
                    items.Add(item);
                }
            }

            snapshot["_items"] = items;
            return snapshot;
        }

        public override void ApplySnapshot(Dictionary<string, object> snapshot)
        {
            if (snapshot == null) return;

            // Очищаем текущий список
            Clear();

            if (snapshot.TryGetValue("_items", out var itemsObj) && itemsObj is List<object> itemsList)
            {
                foreach (var itemObj in itemsList)
                {
                    if (itemObj is Dictionary<string, object> itemDict)
                    {
                        // Это снапшот TrackableNode
                        // В реальности нужно создать объект типа T и применить к нему снапшот
                        // Это сложно без рефлексии, поэтому для простоты пропускаем
                        Debug.LogWarning("SyncList: Cannot deserialize TrackableNode from snapshot without reflection");
                    }
                    else
                    {
                        // Простой объект
                        T item = ConvertValue<T>(itemObj);
                        Add(item);
                    }
                }
            }
        }

        private void ApplySnapshot(object snapshot)
        {
            if (snapshot is Dictionary<string, object> dict)
            {
                ApplySnapshot(dict);
            }
        }

        #endregion

        #region GetValue (для обратной совместимости)

        public override object GetValue(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return this;
            }

            if (path.StartsWith("[") && path.Contains("]"))
            {
                int bracketIndex = path.IndexOf(']');
                string indexStr = path.Substring(1, bracketIndex - 1);

                if (int.TryParse(indexStr, out int index) && index >= 0 && index < _items.Count)
                {
                    if (bracketIndex == path.Length - 1)
                    {
                        return _items[index];
                    }
                    else if (path[bracketIndex + 1] == '.')
                    {
                        string propertyPath = path.Substring(bracketIndex + 2);
                        var item = _items[index];
                        if (item is ITrackable trackable)
                        {
                            return trackable.GetValue(propertyPath);
                        }
                    }
                }
            }

            return null;
        }

        #endregion

        public override string ToString() => $"SyncList<{typeof(T).Name}>[{Count}]";
    }
}