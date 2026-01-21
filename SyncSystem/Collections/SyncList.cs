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

                    OnChanged($"[{index}]", oldValue, value);
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

        #region Переопределение ApplyPatch для обработки снапшотов

        public override void ApplyPatch(string path, object value)
        {
            if (string.IsNullOrEmpty(path))
            {
                // Это может быть снапшот всего списка
                if (value is Dictionary<string, object> dict && dict.ContainsKey("_items"))
                {
                    ApplySnapshot(dict);
                    return;
                }
            }

            // Проверяем, не является ли путь специальным ключом для списка
            if (path == "_list" || path.EndsWith("._list"))
            {
                if (value is Dictionary<string, object> dict)
                {
                    ApplySnapshot(dict);
                    return;
                }
            }

            // Во всех остальных случаях передаем стандартной обработке
            base.ApplyPatch(path, value);
        }

        #endregion

        #region Переопределение BuildSnapshot и CreateSnapshot

        public override void BuildSnapshot(string currentPath, Dictionary<string, object> snapshot)
        {
            // Вместо создания путей вида "Boxes[0].Position",
            // создаем отдельный словарь для списка
            var listData = new Dictionary<string, object>();

            // Сохраняем элементы
            for (int i = 0; i < _items.Count; i++)
            {
                var element = _items[i];

                if (element is TrackableNode node)
                {
                    // Для TrackableNode элементов сохраняем их снапшот
                    var elementSnapshot = node.CreateSnapshot();
                    listData[$"{i}"] = elementSnapshot;
                }
                else
                {
                    // Простые типы сохраняем как есть
                    listData[$"{i}"] = element;
                }
            }

            // Добавляем метаданные
            listData["_count"] = _items.Count;
            listData["_type"] = "SyncList";
            listData["_itemType"] = typeof(T).FullName;

            // Ключ для сохранения в общий снапшот
            // Если currentPath не пустой, добавляем суффикс ._list
            if (string.IsNullOrEmpty(currentPath))
            {
                snapshot["_list"] = listData;
            }
            else
            {
                snapshot[$"{currentPath}._list"] = listData;
            }
        }

        public override Dictionary<string, object> CreateSnapshot()
        {
            var snapshot = new Dictionary<string, object>();
            BuildSnapshot("", snapshot);
            return snapshot;
        }

        #endregion

        #region Применение снапшота

        public override void ApplySnapshot(Dictionary<string, object> snapshot)
        {
            // Ищем данные списка в снапшоте
            Dictionary<string, object> listData = null;

            // Пробуем найти ключ с суффиксом ._list или просто _list
            foreach (var kvp in snapshot)
            {
                if (kvp.Key == "_list" || kvp.Key.EndsWith("._list"))
                {
                    if (kvp.Value is Dictionary<string, object> data)
                    {
                        listData = data;
                        break;
                    }
                }
            }

            if (listData == null)
            {
                // Возможно, snapshot уже содержит данные списка напрямую
                if (snapshot.ContainsKey("_count"))
                {
                    listData = snapshot;
                }
                else
                {
                    Debug.LogError("SyncList: No valid list data found in snapshot");
                    return;
                }
            }

            // Извлекаем количество элементов
            if (!listData.TryGetValue("_count", out var countObj) || !(countObj is int count))
            {
                Debug.LogError("SyncList: Invalid or missing count in snapshot");
                return;
            }

            // Очищаем текущий список
            ClearSilent();

            // Восстанавливаем элементы
            for (int i = 0; i < count; i++)
            {
                var elementKey = i.ToString();
                if (listData.TryGetValue(elementKey, out var elementData))
                {
                    T item;

                    if (elementData is Dictionary<string, object> elementDict)
                    {
                        // Это TrackableNode элемент
                        item = CreateItemFromSnapshot(elementDict);
                    }
                    else
                    {
                        // Простой тип
                        item = JsonGameSerializer.ConvertValue<T>(elementData);
                    }

                    if (item != null)
                    {
                        AddSilent(item);
                    }
                }
            }

            // Уведомляем об изменении
            OnPatched("", _items);
        }

        private T CreateItemFromSnapshot(Dictionary<string, object> elementSnapshot)
        {
            try
            {
                T item = Activator.CreateInstance<T>();

                if (item is TrackableNode node)
                {
                    // Применяем снапшот к элементу
                    node.ApplySnapshot(elementSnapshot);
                }

                return item;
            }
            catch (Exception ex)
            {
                Debug.LogError($"SyncList: Failed to create item from snapshot: {ex.Message}");
                return default(T);
            }
        }

        private void ClearSilent()
        {
            foreach (var item in _items)
            {
                UnsubscribeFromElement(item);
            }

            _items.Clear();
            _elementChangeHandlers.Clear();
        }

        private void AddSilent(T item)
        {
            int index = _items.Count;
            _items.Add(item);
            SubscribeToElement(item, index);
        }

        #endregion

        #region Применение операций (вызываются из сети)

        protected override void ApplyPatchInternal(string[] pathParts, int currentIndex, object value)
        {
            if (currentIndex >= pathParts.Length) return;

            var currentPart = pathParts[currentIndex];

            // Проверяем, является ли текущая часть индексом списка
            if (currentPart.StartsWith("[") && currentPart.EndsWith("]"))
            {
                string indexStr = currentPart.Substring(1, currentPart.Length - 2);
                if (!int.TryParse(indexStr, out int elementIndex))
                {
                    Debug.LogError($"SyncList: Invalid index format: {currentPart}");
                    return;
                }

                if (elementIndex < 0 || elementIndex >= _items.Count)
                {
                    Debug.LogError($"SyncList: Index out of range: {elementIndex} (Count: {_items.Count})");
                    return;
                }

                var element = _items[elementIndex];

                if (currentIndex == pathParts.Length - 1)
                {
                    ApplyReplaceOperation(elementIndex, value);
                    return;
                }

                if (element is ITrackable trackableElement)
                {
                    var remainingPath = string.Join(".", pathParts, currentIndex + 1, pathParts.Length - currentIndex - 1);
                    trackableElement.ApplyPatch(remainingPath, value);
                }
            }
            else if (currentPart.Contains("/"))
            {
                ApplyCollectionOperation(currentPart, value);
            }
            else
            {
                base.ApplyPatchInternal(pathParts, currentIndex, value);
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

        #region GetValue

        public override object GetValue(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return this;
            }

            var parts = path.Split('.');
            return GetValueInternal(parts, 0);
        }

        private object GetValueInternal(string[] pathParts, int currentIndex)
        {
            if (currentIndex >= pathParts.Length) return null;

            var currentPart = pathParts[currentIndex];

            if (currentPart.StartsWith("[") && currentPart.EndsWith("]"))
            {
                string indexStr = currentPart.Substring(1, currentPart.Length - 2);
                if (!int.TryParse(indexStr, out int index) || index < 0 || index >= _items.Count)
                {
                    return null;
                }

                var element = _items[index];

                if (currentIndex == pathParts.Length - 1)
                {
                    return element;
                }

                if (element is ITrackable trackable)
                {
                    var remainingPath = string.Join(".", pathParts, currentIndex + 1, pathParts.Length - currentIndex - 1);
                    return trackable.GetValue(remainingPath);
                }
            }

            return null;
        }

        #endregion

        public override string ToString() => $"SyncList<{typeof(T).Name}>[{Count}]";
    }
}