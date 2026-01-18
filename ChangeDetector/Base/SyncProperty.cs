using System;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector.Base
{
    /// <summary>
    /// Обертка для синхронизируемого свойства с разделением входящих/исходящих изменений
    /// </summary>
    public class SyncProperty<T> : IEquatable<SyncProperty<T>>
    {
        private T _value;
        private readonly string _propertyName;
        private readonly SyncNode _owner;
        private readonly bool _trackChanges; // Генерировать ли исходящие изменения
        private readonly bool _receivePatches; // Принимать ли входящие патчи

        public event Action<T>? ValueChanged; // Срабатывает при ЛЮБОМ изменении
        public event Action<T>? Patched; // Срабатывает только при применении патча из сети

        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value))
                    return;

                var oldValue = _value;
                _value = value;

                // Локальное изменение - генерируем патч для сети
                if (_trackChanges)
                {
                    _owner.RaisePropertyChange(_propertyName, oldValue, value);
                }

                ValueChanged?.Invoke(value);
            }
        }

        public SyncProperty(
            SyncNode owner,
            string propertyName,
            T defaultValue = default!,
            bool trackChanges = true,
            bool receivePatches = true)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _propertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
            _value = defaultValue;
            _trackChanges = trackChanges;
            _receivePatches = receivePatches;
        }

        /// <summary>
        /// Применить патч из сети (без генерации исходящего патча)
        /// </summary>
        public void ApplyPatch(T newValue)
        {
            if (!_receivePatches)
                return;

            if (EqualityComparer<T>.Default.Equals(_value, newValue))
                return;

            _value = newValue;
            Patched?.Invoke(newValue);
            ValueChanged?.Invoke(newValue);
        }

        // Операторы для удобства
        public static implicit operator T(SyncProperty<T> property) => property.Value;

        public override bool Equals(object obj) =>
            obj is SyncProperty<T> other && Equals(other);

        public bool Equals(SyncProperty<T> other) =>
            other != null && EqualityComparer<T>.Default.Equals(_value, other._value);

        public override int GetHashCode() => _value?.GetHashCode() ?? 0;

        public override string ToString() => _value?.ToString() ?? "null";
    }
}