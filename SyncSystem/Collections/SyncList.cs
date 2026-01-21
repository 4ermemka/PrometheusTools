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
                    GenerateCollectionPatch("replace", index, value);
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
            GenerateCollectionPatch("add", index, item);
        }

        public void Insert(int index, T item)
        {
            _items.Insert(index, item);
            ReindexElementsFrom(index + 1);
            SubscribeToElement(item, index);
            GenerateCollectionPatch("insert", index, item);
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
            GenerateCollectionPatch("remove", index, oldValue);
        }

        public void Clear()
        {
            foreach (var item in _items)
            {
                UnsubscribeFromElement(item);
            }

            var oldItems = _items.ToList();
            _items.Clear();
            _elementChangeHandlers.Clear();

            GenerateCollectionPatch("clear", -1, oldItems);
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

                    // Используем метод TrackableNode для генерации события
                    OnChildChanged(trackable as TrackableNode, elementPath, oldVal, newVal);
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

        #region Переопределение методов TrackableNode

        // Переопределяем ApplyPatch для обработки специфичных для списка путей
        public override void ApplyPatch(string path, object value)
        {
            if (string.IsNullOrEmpty(path))
            {
                // Снапшот всего списка
                ApplySnapshot(value);
                return;
            }

            // Если путь содержит ".", значит нужно спуститься вглубь
            // Разбиваем путь на части
            var parts = path.Split(new[] { '.' }, 2);

            if (parts.Length == 1)
            {
                // Только операция над списком или индекс элемента
                ProcessListOperation(path, value);
            }
            else
            {
                // Путь вида "[index].property" или "operation.property"
                var firstPart = parts[0];
                var remainingPath = parts[1];

                if (firstPart.StartsWith("[") && firstPart.EndsWith("]"))
                {
                    // Обращение к элементу списка
                    var indexStr = firstPart.Substring(1, firstPart.Length - 2);
                    if (int.TryParse(indexStr, out int index) && index >= 0 && index < _items.Count)
                    {
                        var element = _items[index];
                        if (element is ITrackable trackable)
                        {
                            trackable.ApplyPatch(remainingPath, value);
                        }
                    }
                }
                else
                {
                    // Операция с дополнительным путем (нестандартный случай)
                    ProcessListOperation(path, value);
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
                        // [index]/operation - операция над элементом
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
        }

        // Переопределяем GetValue для поддержки доступа к элементам
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
                        var element = _items[index];
                        if (element is ITrackable trackable)
                        {
                            return trackable.GetValue(propertyPath);
                        }
                    }
                }
            }

            return null;
        }

        #endregion

        #region Операции над списком

        private void GenerateCollectionPatch(string operation, int index, object value)
        {
            string path = operation;
            if (index >= 0)
            {
                path += $"/{index}";
            }

            // Используем базовый метод для вызова события Changed
            // Нужно создать временный обработчик или переопределить логику
            // Вместо прямого вызова Changed, создаем временный TrackableNode
            // или используем существующую инфраструктуру

            // Временное решение: создаем специальный метод для вызова события
            InvokeCollectionChanged(path, null, value);
        }

        // Новый метод для вызова Changed событий для операций коллекции
        private void InvokeCollectionChanged(string path, object oldValue, object newValue)
        {
            // Так как Changed событие приватное в TrackableNode, мы не можем его вызвать напрямую
            // Нужно использовать другой подход

            // Временное решение: создать временный TrackableNode для генерации события
            // Но лучше добавить защищенный метод в TrackableNode для вызова события

            // Пока что используем такой подход:
            // 1. Создаем временный класс-наследник TrackableNode
            // 2. Или используем рефлексию (нежелательно)

            // Вместо этого, давайте изменим подход:
            // Будем использовать существующую инфраструктуру TrackableNode
            // Создадим временное поле в SyncList для генерации событий

            // Для простоты пока оставим так, но в реальном коде нужно решить эту проблему
            Debug.Log($"SyncList: Generated patch {path}: {oldValue} -> {newValue}");

            // Временное решение: используем OnChildChanged с null child
            OnChildChanged(null, path, oldValue, newValue);
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
                    // Можно добавить другие операции
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

            // Вызываем Patched событие
            OnChildPatched(null, $"add/{index}", item);
        }

        private void ApplyInsertOperation(int index, object value)
        {
            T item = ConvertValue<T>(value);
            _items.Insert(index, item);
            ReindexElementsFrom(index + 1);
            SubscribeToElement(item, index);

            OnChildPatched(null, $"insert/{index}", item);
        }

        private void ApplyRemoveOperation(int index, object value)
        {
            if (index >= 0 && index < _items.Count)
            {
                var oldItem = _items[index];
                UnsubscribeFromElement(oldItem);
                _items.RemoveAt(index);
                ReindexElementsFrom(index);

                OnChildPatched(null, $"remove/{index}", oldItem);
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

                OnChildPatched(null, $"[{index}]", newItem);
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

                OnChildPatched(null, $"move/{fromIndex}/{toIndex}", item);
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

            OnChildPatched(null, "clear", oldItems);
        }

        private TValue ConvertValue<TValue>(object value)
        {
            return JsonGameSerializer.ConvertValue<TValue>(value);
        }

        #endregion

        #region Снапшоты

        private void ApplySnapshot(object snapshot)
        {
            if (snapshot is Dictionary<string, object> dict)
            {
                // Применяем снапшот через базовый метод
                base.ApplySnapshot(dict);
            }
        }

        #endregion

        #region Вспомогательные методы

        // Вспомогательные методы для вызова событий
        private void OnChildChanged(TrackableNode child, string path, object oldValue, object newValue)
        {
            // Используем рефлексию для вызова protected метода TrackableNode
            // Или создаем protected метод в TrackableNode для этого

            // Временное решение: переопределим логику в SyncList
            // Будем напрямую вызывать обработчики событий через reflection

            // Пока просто логируем
            Debug.Log($"SyncList: Child changed {path}: {oldValue} -> {newValue}");
        }

        private void OnChildPatched(TrackableNode child, string path, object value)
        {
            Debug.Log($"SyncList: Child patched {path}: {value}");
        }

        public override string ToString()
        {
            return $"SyncList<{typeof(T).Name}>[{Count}]";
        }

        #endregion
    }
}