using Assets.Scripts.Network.NetCore;
using System;
using System.Collections;
using UnityEngine;

namespace Assets.Shared.SyncSystem.Core
{
    // Конкретная реализация Sync<T>
    public class Sync<T> : SyncBase
    {
        private T _value;

        public event Action<T, T> ValueChanged;

        private event Action<string, object, object> _changed;
        private event Action<string, object> _patched;

        public override event Action<string, object, object> Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public override event Action<string, object> Patched
        {
            add => _patched += value;
            remove => _patched -= value;
        }

        public T Value
        {
            get => _value;
            set
            {
                if (!Equals(_value, value))
                {
                    var oldValue = _value;
                    _value = value;
                    ValueChanged?.Invoke(oldValue, _value);
                    _changed?.Invoke("", oldValue, _value);
                }
            }
        }

        public Sync(T initialValue = default) => _value = initialValue;

        public override void ApplyPatch(string path, object value)
        {
            if (!string.IsNullOrEmpty(path))
                return;

            SetValueSilent(value);
        }

        public override object GetValue(string path) => string.IsNullOrEmpty(path) ? _value : null;

        public override void SetValueSilent(object value)
        {
            _value = JsonGameSerializer.ConvertValue<T>(value);
            _patched?.Invoke("", _value);
        }

        public static implicit operator T(Sync<T> sync) => sync.Value;
        public static implicit operator Sync<T>(T value) => new Sync<T>(value);
    }
}