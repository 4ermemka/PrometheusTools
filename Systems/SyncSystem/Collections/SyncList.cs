﻿using Assets.Scripts.Network.NetCore;
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

        #region Переопределение ApplyPatch для обработки индексов

        protected override bool IsSpecialPath(string pathPart, out string parsedValue)
        {
            // Проверяем, является ли путь индексом списка в формате [index]
            if (pathPart.StartsWith("[") && pathPart.EndsWith("]"))
            {
                parsedValue = pathPart.Substring(1, pathPart.Length - 2);
                return true;
            }

            // Проверяем, является ли путь операцией над списком (add/remove и т.д.)
            if (pathPart.Contains("/"))
            {
                parsedValue = pathPart;
                return true;
            }

            parsedValue = null;
            return false;
        }

        protected override bool HandleSpecialPath(string[] pathParts, int currentIndex, object value)
        {
            var specialPath = pathParts[currentIndex];

            // Обработка индексных путей [index]
            if (specialPath.StartsWith("[") && specialPath.EndsWith("]"))
            {
                return HandleIndexPath(pathParts, currentIndex, value);
            }

            // Обработка операций над списком (add/remove и т.д.)
            if (specialPath.Contains("/"))
            {
                return HandleCollectionOperation(specialPath, value);
            }

            return false;
        }

        private bool HandleIndexPath(string[] pathParts, int currentIndex, object value)
        {
            string indexStr = pathParts[currentIndex].Substring(1, pathParts[currentIndex].Length - 2);

            if (!int.TryParse(indexStr, out int elementIndex))
            {
                Debug.LogError($"SyncList: Invalid index format: {pathParts[currentIndex]}");
                return true; // Путь обработан (ошибка, но обработана)
            }

            // Если индекс выходит за пределы - создаем элементы до нужного индекса
            while (elementIndex >= _items.Count)
            {
                // В режиме снапшота создаем элемент по умолчанию
                T newElement = CreateDefaultElement();
                AddSilent(newElement);
            }

            var element = _items[elementIndex];

            // Если элемент null (возможно, был создан как заглушка), пытаемся создать экземпляр
            if (element == null)
            {
                element = CreateElementFromValue(value, pathParts, currentIndex);
                if (element != null)
                {
                    _items[elementIndex] = element;
                    // Если элемент трекабельный - подписываемся на изменения
                    SubscribeToElement(element, elementIndex);
                }
            }

            // Если это конечный путь - устанавливаем значение элемента
            if (currentIndex == pathParts.Length - 1)
            {
                if (element is ITrackable trackableElement)
                {
                    // Если элемент трекабельный, применяем патч к нему (путь "")
                    trackableElement.ApplyPatch("", value);
                }
                else if (element != null)
                {
                    // Если элемент не трекабельный, но существует - заменяем его
                    ReplaceElementSilently(elementIndex, value);
                }
                else
                {
                    // Если элемент все еще null - создаем и устанавливаем
                    var newElement = CreateElementFromValue(value, pathParts, currentIndex);
                    if (newElement != null)
                    {
                        _items[elementIndex] = newElement;
                        SubscribeToElement(newElement, elementIndex);
                    }
                }
                return true;
            }

            // Если есть дальше вложенные пути и элемент трекабельный
            if (element is ITrackable trackable)
            {
                var remainingPath = string.Join(".", pathParts, currentIndex + 1, pathParts.Length - currentIndex - 1);
                trackable.ApplyPatch(remainingPath, value);
                return true;
            }

            // Если элемент не трекабельный, но есть дальнейший путь - ошибка
            if (currentIndex < pathParts.Length - 1)
            {
                Debug.LogError($"SyncList: Cannot apply nested path to non-trackable element at index {elementIndex}");
            }

            return true;
        }

        private bool HandleCollectionOperation(string operationPath, object value)
        {
            var parts = operationPath.Split('/');
            string operation = parts[0];

            switch (operation)
            {
                case "add":
                    ApplyAddOperation(parts, value);
                    return true;

                case "insert":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int insertIndex))
                    {
                        ApplyInsertOperation(insertIndex, value);
                    }
                    return true;

                case "remove":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int removeIndex))
                    {
                        ApplyRemoveOperation(removeIndex, value);
                    }
                    return true;

                case "replace":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int replaceIndex))
                    {
                        ApplyReplaceOperation(replaceIndex, value);
                    }
                    return true;

                case "move":
                    if (parts.Length > 2 &&
                        int.TryParse(parts[1], out int fromIndex) &&
                        int.TryParse(parts[2], out int toIndex))
                    {
                        ApplyMoveOperation(fromIndex, toIndex);
                    }
                    return true;

                case "clear":
                    ApplyClearOperation();
                    return true;
            }

            return false;
        }

        protected override void ApplyPatchInternal(string[] pathParts, int index, object value)
        {
            // Сначала проверяем, не является ли путь специальным для списка
            if (index < pathParts.Length && HandleSpecialPath(pathParts, index, value))
            {
                return;
            }

            // Если не специальный путь, используем базовую логику
            base.ApplyPatchInternal(pathParts, index, value);
        }

        #endregion

        #region Silent операции (без вызова событий Changed)

        private void AddSilent(T item)
        {
            int index = _items.Count;
            _items.Add(item);
            SubscribeToElement(item, index);
            // Не вызываем OnChanged для снапшота
        }

        private void InsertSilent(int index, T item)
        {
            _items.Insert(index, item);
            ReindexElementsFrom(index + 1);
            SubscribeToElement(item, index);
            // Не вызываем OnChanged для снапшота
        }

        private void RemoveAtSilent(int index)
        {
            var oldValue = _items[index];
            UnsubscribeFromElement(oldValue);
            _items.RemoveAt(index);
            ReindexElementsFrom(index);
            // Не вызываем OnChanged для снапшота
        }

        private void ReplaceElementSilently(int index, object value)
        {
            var oldValue = _items[index];
            UnsubscribeFromElement(oldValue);

            T newItem = JsonGameSerializer.ConvertValue<T>(value);
            _items[index] = newItem;
            SubscribeToElement(newItem, index);

            // Вызываем Patched для уведомления локальных подписчиков
            OnPatched($"[{index}]", newItem);
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

        #endregion

        #region Вспомогательные методы для создания элементов

        // Метод для создания элемента по умолчанию
        private T CreateDefaultElement()
        {
            if (typeof(T).IsValueType)
            {
                // Для значимых типов создаем default
                return default(T);
            }
            else if (typeof(T).IsClass)
            {
                try
                {
                    // Пытаемся создать экземпляр через конструктор без параметров
                    return Activator.CreateInstance<T>();
                }
                catch
                {
                    // Если не удается создать - возвращаем null
                    return default(T);
                }
            }

            return default(T);
        }

        // Метод для создания элемента на основе значения
        private T CreateElementFromValue(object value, string[] pathParts, int currentIndex)
        {
            // Если value уже является T
            if (value is T typedValue)
            {
                return typedValue;
            }

            // Если это TrackableNode и у нас есть информация о типе
            if (typeof(TrackableNode).IsAssignableFrom(typeof(T)))
            {
                try
                {
                    T element = Activator.CreateInstance<T>();

                    // Если у нас есть вложенные пути и элемент - TrackableNode
                    if (currentIndex < pathParts.Length - 1 && element is TrackableNode node)
                    {
                        // Создаем временный словарь для применения патчей
                        var tempSnapshot = new Dictionary<string, object>();
                        var remainingPath = string.Join(".", pathParts, currentIndex + 1, pathParts.Length - currentIndex - 1);
                        tempSnapshot[remainingPath] = value;

                        // Применяем патч к новому элементу
                        node.ApplySnapshot(tempSnapshot);
                    }

                    return element;
                }
                catch
                {
                    // Не удалось создать экземпляр
                    return default(T);
                }
            }

            // Для простых типов конвертируем значение
            try
            {
                return JsonGameSerializer.ConvertValue<T>(value);
            }
            catch
            {
                return default(T);
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

        #region Снапшоты

        public override void BuildSnapshot(string currentPath, Dictionary<string, object> snapshot)
        {
            // Для каждого элемента создаем записи в формате currentPath.[index].propertyPath
            for (int i = 0; i < _items.Count; i++)
            {
                var element = _items[i];
                var elementPath = string.IsNullOrEmpty(currentPath) ? $"[{i}]" : $"{currentPath}.[{i}]";

                if (element is TrackableNode node)
                {
                    // Для TrackableNode элементов собираем их снапшот с правильными путями
                    node.BuildSnapshot(elementPath, snapshot);
                }
                else if (element is SyncBase sync)
                {
                    // Для Sync полей сохраняем значение
                    snapshot[elementPath] = sync.GetValue("");
                }
                else if (element != null)
                {
                    // Для простых типов сохраняем как есть
                    snapshot[elementPath] = element;
                }
                else
                {
                    // Для null элементов сохраняем null
                    snapshot[elementPath] = null;
                }
            }
        }

        public override void ApplySnapshot(Dictionary<string, object> snapshot)
        {
            try
            {
                // Очищаем список
                ClearSilent();

                // Собираем все индексные пути из снапшота
                var indexedData = new Dictionary<int, List<KeyValuePair<string, object>>>();

                foreach (var kvp in snapshot)
                {
                    if (TryParseIndexPath(kvp.Key, out int index, out string remainingPath))
                    {
                        if (!indexedData.ContainsKey(index))
                        {
                            indexedData[index] = new List<KeyValuePair<string, object>>();
                        }

                        indexedData[index].Add(new KeyValuePair<string, object>(remainingPath, kvp.Value));
                    }
                }

                // Обрабатываем данные по индексам
                foreach (var kvp in indexedData.OrderBy(x => x.Key))
                {
                    int index = kvp.Key;
                    var elementData = kvp.Value;

                    // Создаем элемент
                    T element = CreateElementForSnapshot(elementData);

                    // Добавляем элемент в список
                    while (_items.Count <= index)
                    {
                        AddSilent(default(T));
                    }

                    // Заменяем элемент (если уже есть) или устанавливаем
                    if (index < _items.Count)
                    {
                        _items[index] = element;
                    }
                    else
                    {
                        AddSilent(element);
                    }

                    // Подписываемся на изменения элемента
                    SubscribeToElement(element, index);

                    // Применяем данные к элементу
                    ApplyDataToElement(element, elementData);
                }

                OnPatched($"{this.GetType().Name}", _items);
            }
            catch (Exception ex)
            {
                Debug.LogError($"ApplySnapshot exception: {ex.Message}: {ex.StackTrace}");
            }

            // Уведомляем об изменении всего списка
        }

        private T CreateElementForSnapshot(List<KeyValuePair<string, object>> elementData)
        {
            // Сначала ищем прямое значение элемента (путь "")
            var directValue = elementData.FirstOrDefault(x => string.IsNullOrEmpty(x.Key));
            if (!directValue.Equals(default(KeyValuePair<string, object>)))
            {
                // Если есть прямое значение, создаем элемент из него
                return CreateElementFromValue(directValue.Value, new string[0], 0);
            }

            // Если нет прямого значения, создаем элемент по умолчанию
            return CreateDefaultElement();
        }

        private void ApplyDataToElement(T element, List<KeyValuePair<string, object>> elementData)
        {
            if (element == null) return;

            foreach (var data in elementData)
            {
                if (string.IsNullOrEmpty(data.Key))
                {
                    // Прямое значение элемента уже обработано при создании
                    continue;
                }

                // Применяем патч к элементу
                if (element is ITrackable trackable)
                {
                    trackable.ApplyPatch(data.Key, data.Value);
                }
            }
        }

        private bool TryParseIndexPath(string fullPath, out int index, out string remainingPath)
        {
            index = -1;
            remainingPath = null;

            // Ищем первый индекс в пути
            var parts = fullPath.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.StartsWith("[") && part.EndsWith("]"))
                {
                    string indexStr = part.Substring(1, part.Length - 2);
                    if (int.TryParse(indexStr, out index))
                    {
                        // Остаток пути после индекса
                        if (i + 1 < parts.Length)
                        {
                            remainingPath = string.Join(".", parts, i + 1, parts.Length - i - 1);
                        }
                        else
                        {
                            remainingPath = "";
                        }
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        public override string ToString() => $"SyncList<{typeof(T).Name}>[{Count}]";
    }
}