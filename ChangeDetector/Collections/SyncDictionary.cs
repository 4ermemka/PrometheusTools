using System;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector.Collections
{
    /// <summary>
    /// Словарь с поддержкой трекинга и применения патчей.
    /// 
    /// Особенности:
    /// - Bubbling изменений от значений-узлов SyncNode с добавлением ключа в путь;
    /// - Патчи Add/Replace/Remove/Clear по ключам.
    /// </summary>
    public sealed class SyncDictionary<TKey, TValue> : SyncNode, ITrackableCollection
    {
        private readonly Dictionary<TKey, TValue> _inner = new();

        /// <inheritdoc />
        public event Action<CollectionChange>? CollectionChanged;

        public int Count => _inner.Count;
        public ICollection<TKey> Keys => _inner.Keys;
        public ICollection<TValue> Values => _inner.Values;

        /// <summary>
        /// Получение/установка значения по ключу.
        /// При установке:
        /// - если ключ уже был, отписывается от старого значения;
        /// - подписывается на новое значение;
        /// - генерирует патч Add или Replace.
        /// </summary>
        public TValue this[TKey key]
        {
            get => _inner[key];
            set
            {
                var exists = _inner.TryGetValue(key, out var old);
                if (exists)
                    UnwireChild(old);

                _inner[key] = value;
                WireChild(key, value);

                var kind = exists ? CollectionOpKind.Replace : CollectionOpKind.Add;
                CollectionChanged?.Invoke(new CollectionChange(kind, key, value));
                RaiseLocalChange($"[{key}]", old, value);
            }
        }

        /// <summary>
        /// Добавляет новый ключ-значение.
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            _inner.Add(key, value);
            WireChild(key, value);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Add, key, value));
            RaiseLocalChange($"[{key}]", null, value);
        }

        /// <summary>
        /// Удаляет ключ и значение по нему.
        /// </summary>
        public bool Remove(TKey key)
        {
            if (!_inner.TryGetValue(key, out var old))
                return false;

            UnwireChild(old);
            _inner.Remove(key);

            CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Remove, key, old));
            RaiseLocalChange($"[{key}]", old, null);
            return true;
        }

        public bool ContainsKey(TKey key) => _inner.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value);

        /// <summary>
        /// Подписывает значение (если оно SyncNode) на bubbling изменений с ключом в пути.
        /// </summary>
        private void WireChild(TKey key, TValue value)
        {
            if (value is SyncNode node)
            {
                node.Changed += childChange =>
                {
                    var newPath = new List<FieldPathSegment> { new FieldPathSegment($"[{key}]") };
                    newPath.AddRange(childChange.Path);
                    RaiseChange(new FieldChange(newPath, childChange.OldValue, childChange.NewValue));
                };
            }
        }

        /// <summary>
        /// Отписывает значение (если оно SyncNode).
        /// </summary>
        private void UnwireChild(TValue value)
        {
            if (value is SyncNode node)
            {
                // TODO: отписка при необходимости
            }
        }

        /// <summary>
        /// Применяет патч к словарю.
        /// </summary>
        public void ApplyCollectionChange(CollectionChange change)
        {
            switch (change.Kind)
            {
                case CollectionOpKind.Add:
                case CollectionOpKind.Replace:
                    {
                        var key = (TKey)ConvertIfNeeded(change.KeyOrIndex, typeof(TKey));
                        var value = (TValue)ConvertIfNeeded(change.Value, typeof(TValue));
                        this[key] = value;
                        break;
                    }
                case CollectionOpKind.Remove:
                    {
                        var key = (TKey)ConvertIfNeeded(change.KeyOrIndex, typeof(TKey));
                        Remove(key);
                        break;
                    }
                case CollectionOpKind.Clear:
                    {
                        foreach (var kv in _inner)
                            UnwireChild(kv.Value);

                        _inner.Clear();
                        CollectionChanged?.Invoke(new CollectionChange(CollectionOpKind.Clear, null, null));
                        RaiseLocalChange("Clear", null, null);
                        break;
                    }
                default:
                    throw new NotSupportedException("Move для словаря обычно не нужен.");
            }
        }

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