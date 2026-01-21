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

        #region Переопределение ApplyPatch и ApplyPatchInternal

        public override void ApplySnapshot(Dictionary<string, object> snapshot)
        {
            if (snapshot == null) return;

            // Очищаем текущий список
            Clear();

            // Определяем максимальный индекс, чтобы знать, сколько элементов нужно восстановить
            int maxIndex = -1;
            var indexPaths = new List<string>();

            foreach (var key in snapshot.Keys)
            {
                if (key.EndsWith("]"))
                {
                    // Ищем путь с индексом: "Players[0]", "Players[0].Health", "Players[1].Position.x"
                    var bracketIndex = key.LastIndexOf('[');
                    if (bracketIndex != -1)
                    {
                        var closeBracketIndex = key.IndexOf(']', bracketIndex);
                        if (closeBracketIndex != -1)
                        {
                            var indexStr = key.Substring(bracketIndex + 1, closeBracketIndex - bracketIndex - 1);
                            if (int.TryParse(indexStr, out int index))
                            {
                                maxIndex = Math.Max(maxIndex, index);
                                indexPaths.Add(key);
                            }
                        }
                    }
                }
            }

            // Создаем элементы для каждого индекса
            for (int i = 0; i <= maxIndex; i++)
            {
                // Ищем все пути, начинающиеся с текущего индекса
                var indexKey = $"[{i}]";
                var elementPaths = indexPaths.Where(p => p.Contains($"[{i}]")).ToList();

                if (elementPaths.Count > 0)
                {
                    // Создаем элемент
                    T item = CreateItemFromSnapshot(snapshot, elementPaths, i);
                    if (item != null)
                    {
                        // Добавляем в список (без генерации событий)
                        AddSilent(item);
                    }
                }
            }

            // Уведомляем об изменении
            OnPatched("", _items);
        }

        // Ключевое изменение: переопределяем ApplyPatchInternal для обработки индексов
        protected override void ApplyPatchInternal(string[] pathParts, int currentIndex, object value)
        {
            if (currentIndex >= pathParts.Length) return;

            var currentPart = pathParts[currentIndex];

            // Проверяем, является ли текущая часть индексом списка (формат "[index]")
            if (currentPart.StartsWith("[") && currentPart.EndsWith("]"))
            {
                // Извлекаем индекс из строки вида "[index]"
                string indexStr = currentPart.Substring(1, currentPart.Length - 2);
                if (!int.TryParse(indexStr, out int elementIndex))
                {
                    Debug.LogError($"SyncList: Invalid index format: {currentPart}");
                    return;
                }

                // Проверяем границы
                if (elementIndex < 0 || elementIndex >= _items.Count)
                {
                    Debug.LogError($"SyncList: Index out of range: {elementIndex} (Count: {_items.Count})");
                    return;
                }

                // Получаем элемент
                var element = _items[elementIndex];

                // Если это последняя часть пути - заменяем элемент
                if (currentIndex == pathParts.Length - 1)
                {
                    ApplyReplaceOperation(elementIndex, value);
                    return;
                }

                // Если элемент ITrackable, передаем ему оставшийся путь
                if (element is ITrackable trackableElement)
                {
                    // Формируем оставшийся путь
                    var remainingPath = string.Join(".", pathParts, currentIndex + 1, pathParts.Length - currentIndex - 1);
                    trackableElement.ApplyPatch(remainingPath, value);
                }
            }
            else if (currentPart.Contains("/"))
            {
                // Это операция над списком (add/0, remove/2 и т.д.)
                ApplyCollectionOperation(currentPart, value);
            }
            else
            {
                // Неизвестный формат - пробуем базовую обработку
                base.ApplyPatchInternal(pathParts, currentIndex, value);
            }
        }

        #endregion

        #region Применение операций (вызываются из сети)

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

        #region Снапшоты

        public override Dictionary<string, object> CreateSnapshot()
        {
            var snapshot = new Dictionary<string, object>();
            BuildSnapshot("", snapshot);
            return snapshot;
        }

        private T CreateItemFromSnapshot(Dictionary<string, object> snapshot, List<string> elementPaths, int index)
        {
            // Для простых типов
            if (typeof(T).IsPrimitive || typeof(T) == typeof(string) || typeof(T) == typeof(Vector2Dto))
            {
                // Ищем прямое значение для этого индекса
                var directKey = $"[{index}]";
                if (snapshot.TryGetValue(directKey, out var value))
                {
                    return JsonGameSerializer.ConvertValue<T>(value);
                }
            }
            // Для TrackableNode типов
            else if (typeof(TrackableNode).IsAssignableFrom(typeof(T)))
            {
                try
                {
                    T item = Activator.CreateInstance<T>();
                    if (item is TrackableNode node)
                    {
                        // Создаем под-снапшот для этого элемента
                        var elementSnapshot = new Dictionary<string, object>();

                        foreach (var path in elementPaths)
                        {
                            // Преобразуем "Players[0].Health" → "Health"
                            var afterBracket = path.Substring(path.IndexOf(']') + 1);
                            if (afterBracket.StartsWith("."))
                            {
                                var propertyPath = afterBracket.Substring(1);
                                elementSnapshot[propertyPath] = snapshot[path];
                            }
                        }

                        node.ApplySnapshot(elementSnapshot);
                        return item;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to create item from snapshot for index {index}: {ex.Message}");
                }
            }

            return default(T);
        }

        // Вспомогательный метод для добавления без генерации событий
        private void AddSilent(T item)
        {
            int index = _items.Count;
            _items.Add(item);
            SubscribeToElement(item, index);
        }
        
        public override void BuildSnapshot(string currentPath, Dictionary<string, object> snapshot)
        {
            // Сохраняем элементы списка с индексами
            for (int i = 0; i < _items.Count; i++)
            {
                var element = _items[i];
                var elementPath = string.IsNullOrEmpty(currentPath) ? $"[{i}]" : $"{currentPath}[{i}]";

                if (element is TrackableNode node)
                {
                    // Рекурсивно сохраняем TrackableNode элементы
                    node.BuildSnapshot(elementPath, snapshot);
                }
                else if (element is ITrackable trackable)
                {
                    // Для простых ITrackable сохраняем их значение
                    // Но это сложно без общего метода GetValue в ITrackable
                    // Поэтому используем обходной путь через JSON
                    try
                    {
                        string json = JsonGameSerializer.Serialize(element);
                        snapshot[elementPath] = json;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to serialize element at index {i}: {ex.Message}");
                    }
                }
                else
                {
                    // Простые типы сохраняем как есть
                    snapshot[elementPath] = element;
                }
            }

            // Также сохраняем операционные поля списка, если они есть
            // Например, счетчик элементов или другие метаданные
            var countPath = string.IsNullOrEmpty(currentPath) ? "_count" : $"{currentPath}._count";
            snapshot[countPath] = _items.Count;
        }

        #endregion

        #region GetValue

        public override object GetValue(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return this;
            }

            // Разбиваем путь на части
            var parts = path.Split('.');
            return GetValueInternal(parts, 0);
        }

        private object GetValueInternal(string[] pathParts, int currentIndex)
        {
            if (currentIndex >= pathParts.Length) return null;

            var currentPart = pathParts[currentIndex];

            // Проверяем, является ли текущая часть индексом
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