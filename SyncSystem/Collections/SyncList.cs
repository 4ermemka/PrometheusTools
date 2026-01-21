using Assets.Scripts.Network.NetCore;
using Assets.Shared.SyncSystem.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace Assets.Shared.SyncSystem.Collections
{
    public class SyncList<T> : ITrackable, IList<T>
    {
        private readonly List<T> _items = new List<T>();
        private readonly Dictionary<T, ITrackable> _trackableItems = new Dictionary<T, ITrackable>();
        private readonly Dictionary<T, Action<string, object, object>> _changeHandlers = new Dictionary<T, Action<string, object, object>>();

        // ITrackable события
        private event Action<string, object, object> _changed;
        private event Action<string, object> _patched;

        public event Action<string, object, object> Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public event Action<string, object> Patched
        {
            add => _patched += value;
            remove => _patched -= value;
        }

        #region IList<T> Implementation (полная совместимость с List<T>)

        public T this[int index]
        {
            get => _items[index];
            set
            {
                var oldValue = _items[index];
                if (!Equals(oldValue, value))
                {
                    UnsubscribeFromItem(oldValue);
                    _items[index] = value;
                    SubscribeToItem(value, index);

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
            SubscribeToItem(item, index);
            GenerateCollectionPatch("add", index, item);
        }

        public void Insert(int index, T item)
        {
            _items.Insert(index, item);
            ShiftIndices(index + 1);
            SubscribeToItem(item, index);
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
            var oldItem = _items[index];
            UnsubscribeFromItem(oldItem);
            _items.RemoveAt(index);
            ShiftIndices(index);
            GenerateCollectionPatch("remove", index, oldItem);
        }

        public void Clear()
        {
            foreach (var item in _items)
            {
                UnsubscribeFromItem(item);
            }

            var oldItems = _items.ToList();
            _items.Clear();
            _trackableItems.Clear();
            _changeHandlers.Clear();

            GenerateCollectionPatch("clear", -1, oldItems);
        }

        public bool Contains(T item) => _items.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public int IndexOf(T item) => _items.IndexOf(item);

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        #endregion

        #region Подписка на изменения элементов

        private void SubscribeToItem(T item, int index)
        {
            if (item == null) return;

            // Если элемент - ITrackable, подписываемся на его изменения
            if (item is ITrackable trackable)
            {
                _trackableItems[item] = trackable;

                Action<string, object, object> handler = (path, oldValue, newValue) =>
                {
                    // Формируем путь вида "[index].property" или "[index]"
                    string itemPath = $"[{index}]";
                    if (!string.IsNullOrEmpty(path))
                    {
                        itemPath += $".{path}";
                    }

                    _changed?.Invoke(itemPath, oldValue, newValue);
                };

                trackable.Changed += handler;
                _changeHandlers[item] = handler;
            }
        }

        private void UnsubscribeFromItem(T item)
        {
            if (item == null || !_changeHandlers.TryGetValue(item, out var handler))
                return;

            if (_trackableItems.TryGetValue(item, out var trackable))
            {
                trackable.Changed -= handler;
            }

            _trackableItems.Remove(item);
            _changeHandlers.Remove(item);
        }

        private void ShiftIndices(int fromIndex)
        {
            // Переподписываем все элементы, начиная с fromIndex
            for (int i = fromIndex; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item != null && _changeHandlers.TryGetValue(item, out var oldHandler))
                {
                    // Отписываемся со старым индексом
                    if (_trackableItems.TryGetValue(item, out var trackable))
                    {
                        trackable.Changed -= oldHandler;
                    }

                    // Подписываемся с новым индексом
                    SubscribeToItem(item, i);
                }
            }
        }

        #endregion

        #region ITrackable Implementation

        public void ApplyPatch(string path, object value)
        {
            if (string.IsNullOrEmpty(path))
            {
                // Применение снапшота всего списка
                ApplySnapshot(value);
                return;
            }

            // Разбираем путь: [index] или [index].property или операция/параметры
            if (path.StartsWith("["))
            {
                // Путь к элементу списка
                ApplyElementPatch(path, value);
            }
            else if (path.Contains("/"))
            {
                // Операция над списком (add/3, remove/2 и т.д.)
                ApplyCollectionOperation(path, value);
            }
            else
            {
                Debug.LogError($"SyncList: Unknown path format: {path}");
            }
        }

        public object GetValue(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return this; // или список элементов?
            }

            // Обработка путей к элементам
            if (path.StartsWith("[") && path.Contains("]"))
            {
                var bracketIndex = path.IndexOf(']');
                if (bracketIndex > 0)
                {
                    string indexStr = path.Substring(1, bracketIndex - 1);
                    if (int.TryParse(indexStr, out int index) && index >= 0 && index < _items.Count)
                    {
                        // Если только индекс: [index]
                        if (bracketIndex == path.Length - 1)
                        {
                            return _items[index];
                        }

                        // Если есть свойство: [index].property
                        if (path.Length > bracketIndex + 1 && path[bracketIndex + 1] == '.')
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
            }

            return null;
        }

        #endregion

        #region Применение операций над списком (без генерации Changed)

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
                    ApplyInsertOperation(parts, value);
                    break;

                case "remove":
                    ApplyRemoveOperation(parts, value);
                    break;

                case "replace":
                    ApplyReplaceOperation(parts, value);
                    break;

                case "move":
                    ApplyMoveOperation(parts, value);
                    break;

                case "clear":
                    ApplyClearOperation(value);
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
            ShiftIndices(index + 1);
            SubscribeToItem(item, index);
            _patched?.Invoke($"add/{index}", item);
        }

        private void ApplyInsertOperation(string[] parts, object value)
        {
            if (parts.Length > 1 && int.TryParse(parts[1], out int index))
            {
                T item = ConvertValue<T>(value);
                _items.Insert(index, item);
                ShiftIndices(index + 1);
                SubscribeToItem(item, index);
                _patched?.Invoke($"insert/{index}", item);
            }
        }

        private void ApplyRemoveOperation(string[] parts, object value)
        {
            if (parts.Length > 1 && int.TryParse(parts[1], out int index))
            {
                if (index >= 0 && index < _items.Count)
                {
                    var oldItem = _items[index];
                    UnsubscribeFromItem(oldItem);
                    _items.RemoveAt(index);
                    ShiftIndices(index);
                    _patched?.Invoke($"remove/{index}", oldItem);
                }
            }
        }

        private void ApplyReplaceOperation(string[] parts, object value)
        {
            if (parts.Length > 1 && int.TryParse(parts[1], out int index))
            {
                if (index >= 0 && index < _items.Count)
                {
                    var oldItem = _items[index];
                    UnsubscribeFromItem(oldItem);

                    T newItem = ConvertValue<T>(value);
                    _items[index] = newItem;
                    SubscribeToItem(newItem, index);

                    _patched?.Invoke($"replace/{index}", newItem);
                }
            }
        }

        private void ApplyMoveOperation(string[] parts, object value)
        {
            if (parts.Length > 2 &&
                int.TryParse(parts[1], out int fromIndex) &&
                int.TryParse(parts[2], out int toIndex))
            {
                if (fromIndex >= 0 && fromIndex < _items.Count &&
                    toIndex >= 0 && toIndex < _items.Count && fromIndex != toIndex)
                {
                    var item = _items[fromIndex];
                    _items.RemoveAt(fromIndex);

                    // Корректируем toIndex если fromIndex был меньше
                    int adjustedToIndex = toIndex;
                    if (fromIndex < toIndex)
                    {
                        adjustedToIndex--;
                    }

                    _items.Insert(adjustedToIndex, item);

                    // Переподписываем элементы между fromIndex и adjustedToIndex
                    int start = Math.Min(fromIndex, adjustedToIndex);
                    int end = Math.Max(fromIndex, adjustedToIndex);
                    for (int i = start; i <= end; i++)
                    {
                        var elem = _items[i];
                        if (elem != null && _changeHandlers.TryGetValue(elem, out var oldHandler))
                        {
                            if (_trackableItems.TryGetValue(elem, out var trackable))
                            {
                                trackable.Changed -= oldHandler;
                            }
                            SubscribeToItem(elem, i);
                        }
                    }

                    _patched?.Invoke($"move/{fromIndex}/{toIndex}", item);
                }
            }
        }

        private void ApplyClearOperation(object value)
        {
            var oldItems = _items.ToList();

            foreach (var item in _items)
            {
                UnsubscribeFromItem(item);
            }

            _items.Clear();
            _trackableItems.Clear();
            _changeHandlers.Clear();

            _patched?.Invoke("clear", oldItems);
        }

        private void ApplyElementPatch(string elementPath, object value)
        {
            // Формат: [index] или [index].property
            if (elementPath.StartsWith("[") && elementPath.Contains("]"))
            {
                int bracketIndex = elementPath.IndexOf(']');
                string indexStr = elementPath.Substring(1, bracketIndex - 1);

                if (int.TryParse(indexStr, out int index) && index >= 0 && index < _items.Count)
                {
                    if (bracketIndex == elementPath.Length - 1)
                    {
                        // [index] - замена всего элемента
                        ApplyReplaceOperation(new[] { "replace", indexStr }, value);
                    }
                    else if (elementPath[bracketIndex + 1] == '.')
                    {
                        // [index].property - изменение свойства элемента
                        string propertyPath = elementPath.Substring(bracketIndex + 2);
                        var item = _items[index];
                        if (item is ITrackable trackable)
                        {
                            trackable.ApplyPatch(propertyPath, value);
                        }
                    }
                }
            }
        }

        private void ApplySnapshot(object snapshot)
        {
            if (snapshot is JArray jArray)
            {
                // Десериализация из JSON
                Clear();
                foreach (var token in jArray)
                {
                    T item = ConvertValue<T>(token);
                    _items.Add(item);
                }

                // Подписываемся на все элементы
                for (int i = 0; i < _items.Count; i++)
                {
                    SubscribeToItem(_items[i], i);
                }

                _patched?.Invoke("", _items);
            }
            else if (snapshot is List<object> list)
            {
                Clear();
                foreach (var obj in list)
                {
                    T item = ConvertValue<T>(obj);
                    _items.Add(item);
                }

                for (int i = 0; i < _items.Count; i++)
                {
                    SubscribeToItem(_items[i], i);
                }

                _patched?.Invoke("", _items);
            }
        }

        private TValue ConvertValue<TValue>(object value)
        {
            return JsonGameSerializer.ConvertValue<TValue>(value);
        }

        #endregion

        #region Генерация патчей (для локальных изменений)

        private void GenerateCollectionPatch(string operation, int index, object value)
        {
            string path = operation;
            if (index >= 0)
            {
                path += $"/{index}";
            }

            _changed?.Invoke(path, null, value);
        }

        #endregion

        #region Методы для снапшотов

        public List<object> CreateSnapshot()
        {
            var snapshot = new List<object>();
            foreach (var item in _items)
            {
                // Для ITrackable элементов создаем их снапшот
                if (item is ITrackable trackable)
                {
                    // Нужно привести trackable к его конкретному типу для создания снапшота
                    // Это сложно без рефлексии, поэтому используем JSON как промежуточный формат
                    string json = JsonGameSerializer.Serialize(item);
                    snapshot.Add(json);
                }
                else
                {
                    snapshot.Add(item);
                }
            }
            return snapshot;
        }

        #endregion

        #region Вспомогательные методы

        public override string ToString()
        {
            return $"SyncList<{typeof(T).Name}>[{Count}]";
        }

        #endregion
    }
}