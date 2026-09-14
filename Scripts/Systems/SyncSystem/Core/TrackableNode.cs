using System;
using System.Collections.Generic;
using System.Reflection;

namespace Assets.Shared.SyncSystem.Core
{
    public abstract class TrackableNode : ITrackable
    {
        private sealed class TrackableField
        {
            public string Name { get; set; }
            public ITrackable Trackable { get; set; }
        }

        private bool _initialized;
        private readonly Dictionary<string, TrackableField> _trackableFields = new Dictionary<string, TrackableField>();

        private event Action<string, object, object> _changed;
        private event Action<string, object> _patched;

        public event Action<string, object, object> Changed
        {
            add
            {
                EnsureTrackingInitialized();
                _changed += value;
            }
            remove => _changed -= value;
        }

        public event Action<string, object> Patched
        {
            add
            {
                EnsureTrackingInitialized();
                _patched += value;
            }
            remove => _patched -= value;
        }

        protected virtual void OnChanged(string path, object oldValue, object newValue)
        {
            if (!SyncMutationScope.IsSilent)
            {
                _changed?.Invoke(path, oldValue, newValue);
            }
        }

        protected virtual void OnPatched(string path, object value)
        {
            _patched?.Invoke(path, value);
        }

        protected TrackableNode()
        {
        }

        protected void EnsureTrackingInitialized()
        {
            if (_initialized) return;

            _trackableFields.Clear();
            var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (!typeof(ITrackable).IsAssignableFrom(field.FieldType))
                    continue;

                if (!(field.GetValue(this) is ITrackable trackable))
                    continue;

                var fieldName = field.Name;
                _trackableFields[fieldName] = new TrackableField
                {
                    Name = fieldName,
                    Trackable = trackable
                };

                trackable.Changed += (path, oldValue, newValue) =>
                {
                    var fullPath = string.IsNullOrEmpty(path) ? fieldName : $"{fieldName}.{path}";
                    OnChanged(fullPath, oldValue, newValue);
                };

                trackable.Patched += (path, value) =>
                {
                    var fullPath = string.IsNullOrEmpty(path) ? fieldName : $"{fieldName}.{path}";
                    OnPatched(fullPath, value);
                };
            }

            _initialized = true;
        }

        public virtual void ApplyPatch(string path, object value)
        {
            if (string.IsNullOrEmpty(path))
                return;

            EnsureTrackingInitialized();
            using (SyncMutationScope.EnterSilent())
            {
                var parts = path.Split('.');
                ApplyPatchInternal(parts, 0, value);
            }
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
                trackable.ApplyPatch("", value);
                return;
            }

            if (CanHandleRecursivePath(trackable, pathParts, index))
            {
                HandleRecursivePath(trackable, pathParts, index, value);
            }
        }

        protected virtual bool CanHandleRecursivePath(ITrackable trackable, string[] pathParts, int currentIndex)
        {
            return trackable is TrackableNode;
        }

        protected virtual void HandleRecursivePath(ITrackable trackable, string[] pathParts, int currentIndex, object value)
        {
            if (trackable is TrackableNode node)
            {
                node.EnsureTrackingInitialized();
                node.ApplyPatchInternal(pathParts, currentIndex + 1, value);
            }
        }

        public virtual object GetValue(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            EnsureTrackingInitialized();
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
                return trackable.GetValue("");

            if (trackable is TrackableNode node)
            {
                node.EnsureTrackingInitialized();
                return node.GetValueInternal(pathParts, index + 1);
            }

            return null;
        }

        public virtual Dictionary<string, object> CreateSnapshot()
        {
            var snapshot = new Dictionary<string, object>();
            BuildSnapshot("", snapshot);
            return snapshot;
        }

        public virtual void BuildSnapshot(string currentPath, Dictionary<string, object> snapshot)
        {
            EnsureTrackingInitialized();

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
            if (snapshot == null) return;

            using (SyncMutationScope.EnterSilent())
            {
                foreach (var kvp in snapshot)
                {
                    ApplyPatch(kvp.Key, kvp.Value);
                }
            }

            OnPatched(GetType().Name, snapshot);
        }

        protected virtual ITrackable GetTrackableField(string fieldName)
        {
            EnsureTrackingInitialized();
            _trackableFields.TryGetValue(fieldName, out var fieldInfo);
            return fieldInfo?.Trackable;
        }

        protected virtual bool IsSpecialPath(string pathPart, out string parsedValue)
        {
            parsedValue = null;
            return false;
        }

        protected virtual bool HandleSpecialPath(string[] pathParts, int currentIndex, object value)
        {
            return false;
        }
    }
}
