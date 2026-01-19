using System;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector
{
    public class SyncProperty<T> : IEquatable<SyncProperty<T>>
    {
        private T _value;
        private readonly string _propertyName;
        private readonly SyncNode _owner;
        private readonly bool _trackChanges;
        private readonly bool _receivePatches;

        public event Action<T>? ValueChanged;
        public event Action<T>? Patched;

        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value))
                    return;

                var oldValue = _value;
                _value = value;

                if (_trackChanges && _owner != null)
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

        public void ApplyPatch(T newValue)
        {
            if (!_receivePatches) return;

            if (EqualityComparer<T>.Default.Equals(_value, newValue))
                return;

            _value = newValue;
            Patched?.Invoke(newValue);
            ValueChanged?.Invoke(newValue);
        }

        public T GetSnapshotValue() => _value;

        public void ApplySnapshot(T snapshotValue) => _value = snapshotValue;

        public static implicit operator T(SyncProperty<T> property) => property.Value;

        public override bool Equals(object obj) =>
            obj is SyncProperty<T> other && Equals(other);

        public bool Equals(SyncProperty<T> other) =>
            other != null && EqualityComparer<T>.Default.Equals(_value, other._value);

        public override int GetHashCode() => _value?.GetHashCode() ?? 0;

        public override string ToString() => _value?.ToString() ?? "null";
    }
}