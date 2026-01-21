using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Assets.Shared.SyncSystem.Core
{
    public abstract class TrackableNode : ITrackable
    {
        private class TrackableField
        {
            public string Name { get; set; }
            public ITrackable Trackable { get; set; }
        }

        private bool _initialized = false;
        private readonly Dictionary<string, TrackableField> _trackableFields = new Dictionary<string, TrackableField>();

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

        protected virtual void OnChanged(string path, object oldValue, object newValue)
        {
            _changed?.Invoke(path, oldValue, newValue);
        }

        protected virtual void OnPatched(string path, object value)
        {
            _patched?.Invoke(path, value);
        }

        protected TrackableNode() => InitializeTracking();

        protected virtual void InitializeTracking()
        {
            if (_initialized) return;

            var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (typeof(ITrackable).IsAssignableFrom(field.FieldType))
                {
                    var trackable = field.GetValue(this) as ITrackable;
                    if (trackable != null)
                    {
                        var fieldName = field.Name;
                        var trackableField = new TrackableField
                        {
                            Name = fieldName,
                            Trackable = trackable
                        };

                        _trackableFields[fieldName] = trackableField;

                        // Подписываемся на Changed детей для всплытия
                        trackable.Changed += (path, oldVal, newVal) =>
                        {
                            var fullPath = string.IsNullOrEmpty(path) ? fieldName : $"{fieldName}.{path}";
                            _changed?.Invoke(fullPath, oldVal, newVal);
                        };
                    }
                }
            }

            _initialized = true;
        }

        public virtual void ApplyPatch(string path, object value)
        {
            if (string.IsNullOrEmpty(path))
                return;

            var parts = path.Split('.');
            ApplyPatchInternal(parts, 0, value);
        }

        protected virtual void ApplyPatchInternal(string[] pathParts, int index, object value)
        {
            if (index >= pathParts.Length) return;

            var currentPart = pathParts[index];

            if (!_trackableFields.TryGetValue(currentPart, out var fieldInfo))
                return;

            var trackable = fieldInfo.Trackable;

            if (index == pathParts.Length - 1)
            {
                // Конечный элемент - применяем патч здесь
                trackable.ApplyPatch("", value);
            }
            else if (CanHandleRecursivePath(trackable, pathParts, index))
            {
                // Рекурсивно спускаемся дальше
                HandleRecursivePath(trackable, pathParts, index, value);
            }
        }

        // Добавляем виртуальные методы для расширения логики
        protected virtual bool CanHandleRecursivePath(ITrackable trackable, string[] pathParts, int currentIndex)
        {
            // По умолчанию только TrackableNode могут обрабатывать рекурсивные пути
            return trackable is TrackableNode;
        }

        protected virtual void HandleRecursivePath(ITrackable trackable, string[] pathParts, int currentIndex, object value)
        {
            if (trackable is TrackableNode node)
            {
                node.ApplyPatchInternal(pathParts, currentIndex + 1, value);
            }
        }
        
        public virtual object GetValue(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('.');
            return GetValueInternal(parts, 0);
        }

        private object GetValueInternal(string[] pathParts, int index)
        {
            if (index >= pathParts.Length) return null;

            var currentPart = pathParts[index];

            if (!_trackableFields.TryGetValue(currentPart, out var fieldInfo))
                return null;

            var trackable = fieldInfo.Trackable;

            if (index == pathParts.Length - 1)
            {
                return trackable.GetValue("");
            }
            else if (trackable is TrackableNode node)
            {
                return node.GetValueInternal(pathParts, index + 1);
            }

            return null;
        }

        // Методы для снапшотов
        public virtual Dictionary<string, object> CreateSnapshot()
        {
            var snapshot = new Dictionary<string, object>();
            BuildSnapshot("", snapshot);
            return snapshot;
        }

        public virtual void BuildSnapshot(string currentPath, Dictionary<string, object> snapshot)
        {
            foreach (var kvp in _trackableFields)
            {
                var fieldName = kvp.Key;
                var trackable = kvp.Value.Trackable;
                var fullPath = string.IsNullOrEmpty(currentPath) ? fieldName : $"{currentPath}.{fieldName}";

                if (trackable is SyncBase sync)
                {
                    snapshot[fullPath] = sync.GetValue("");
                }
                else if (trackable is TrackableNode node)
                {
                    node.BuildSnapshot(fullPath, snapshot);
                }
            }
        }

        public virtual void ApplySnapshot(Dictionary<string, object> snapshot)
        {
            foreach (var kvp in snapshot)
            {
                ApplyPatch(kvp.Key, kvp.Value);
            }
        }
    }
}