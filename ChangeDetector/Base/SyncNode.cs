using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Assets.Shared.ChangeDetector
{
    public abstract class SyncNode : TrackableNode
    {
        public event Action Patched;
        public event Action SnapshotApplied;

        private readonly Dictionary<string, object> _syncProperties = new();
        private static readonly ConcurrentDictionary<Type, List<PropertyInfo>> _syncPropertiesCache = new();

        protected SyncNode()
        {
            InitializeSyncProperties();
        }

        private void InitializeSyncProperties()
        {
            var type = GetType();
            var properties = GetSyncProperties(type);

            foreach (var prop in properties)
            {
                var syncFieldAttr = prop.GetCustomAttribute<SyncFieldAttribute>(true);
                if (syncFieldAttr == null) continue;

                if (!IsSyncProperty(prop.PropertyType)) continue;

                var propertyType = prop.PropertyType.GetGenericArguments()[0];
                var syncProperty = CreateSyncProperty(prop.Name, propertyType, syncFieldAttr);

                _syncProperties[prop.Name] = syncProperty;
                prop.SetValue(this, syncProperty);

                SubscribeToPatchedEvents(prop.Name, syncProperty);
            }
        }

        private bool IsSyncProperty(Type type)
        {
            return type.IsGenericType &&
                   type.GetGenericTypeDefinition() == typeof(SyncProperty<>);
        }

        private object CreateSyncProperty(string propertyName, Type propertyType, SyncFieldAttribute attr)
        {
            var syncPropertyType = typeof(SyncProperty<>).MakeGenericType(propertyType);
            var defaultValue = propertyType.IsValueType ? Activator.CreateInstance(propertyType) : null;

            return Activator.CreateInstance(
                syncPropertyType,
                this,
                propertyName,
                defaultValue,
                attr.TrackChanges,
                attr.ReceivePatches
            );
        }

        private void SubscribeToPatchedEvents(string propertyName, object syncProperty)
        {
            var syncPropertyType = syncProperty.GetType();
            var patchedEvent = syncPropertyType.GetEvent("Patched");

            if (patchedEvent != null)
            {
                var handler = CreatePatchedHandler(propertyName, syncPropertyType);
                patchedEvent.AddEventHandler(syncProperty, handler);
            }
        }

        private Delegate CreatePatchedHandler(string propertyName, Type syncPropertyType)
        {
            var valueType = syncPropertyType.GetGenericArguments()[0];
            var handlerMethod = GetType().GetMethod("CreatePatchedHandlerGeneric",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var genericMethod = handlerMethod.MakeGenericMethod(valueType);
            return (Delegate)genericMethod.Invoke(this, new object[] { propertyName });
        }

        private Action<T> CreatePatchedHandlerGeneric<T>(string propertyName)
        {
            return value =>
            {
                Patched?.Invoke();
                OnPropertyPatched(propertyName, value);
            };
        }

        protected virtual void OnPropertyPatched(string propertyName, object value)
        {
            // Переопределите если нужно
        }

        private static List<PropertyInfo> GetSyncProperties(Type type)
        {
            return _syncPropertiesCache.GetOrAdd(type, t =>
            {
                return t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(p => p.GetCustomAttribute<SyncFieldAttribute>(true) != null)
                    .ToList();
            });
        }

        // =============== ПАТЧИ ===============

        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object newValue)
        {
            if (path == null || path.Count == 0)
                throw new ArgumentException("Path must be non-empty");

            ApplyPatchInternal(path, 0, newValue);
        }

        private void ApplyPatchInternal(IReadOnlyList<FieldPathSegment> path, int index, object newValue)
        {
            var segment = path[index];
            var propertyName = segment.Name;

            if (!_syncProperties.TryGetValue(propertyName, out var syncProperty))
                throw new InvalidOperationException($"Property '{propertyName}' not found");

            var isLast = index == path.Count - 1;

            if (isLast)
            {
                ApplyPatchToSyncProperty(syncProperty, newValue);
                Patched?.Invoke();
            }
            else
            {
                // Получаем значение SyncProperty
                var valueProperty = syncProperty.GetType().GetProperty("Value");
                var childValue = valueProperty?.GetValue(syncProperty);

                if (childValue is SyncNode childNode)
                {
                    childNode.ApplyPatchInternal(path, index + 1, newValue);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Property '{propertyName}' is not a SyncNode");
                }
            }
        }

        private void ApplyPatchToSyncProperty(object syncProperty, object newValue)
        {
            var applyPatchMethod = syncProperty.GetType().GetMethod("ApplyPatch");
            applyPatchMethod?.Invoke(syncProperty, new[] { newValue });
        }

        // =============== СНАПШОТЫ ===============

        public void ApplySnapshot(SyncNode source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var sourceProperties = GetSyncProperties(source.GetType());
            var targetProperties = GetSyncProperties(GetType());

            foreach (var prop in targetProperties)
            {
                var sourceProp = sourceProperties.FirstOrDefault(p => p.Name == prop.Name);
                if (sourceProp == null) continue;

                ApplySnapshotToProperty(prop, sourceProp, source);
            }

            SnapshotApplied?.Invoke();
        }

        private void ApplySnapshotToProperty(PropertyInfo targetProp, PropertyInfo sourceProp, SyncNode source)
        {
            var targetValue = targetProp.GetValue(this);
            var sourceValue = sourceProp.GetValue(source);

            if (targetValue == null || sourceValue == null) return;

            var targetSyncProperty = targetValue;
            var sourceSyncProperty = sourceValue;

            // Получаем значения из SyncProperty
            var valueProperty = targetSyncProperty.GetType().GetProperty("Value");
            var targetInnerValue = valueProperty?.GetValue(targetSyncProperty);
            var sourceInnerValue = valueProperty?.GetValue(sourceSyncProperty);

            // Применяем снапшот
            var applySnapshotMethod = targetSyncProperty.GetType().GetMethod("ApplySnapshot");
            applySnapshotMethod?.Invoke(targetSyncProperty, new[] { sourceInnerValue });
        }

        // =============== СЕРИАЛИЗАЦИЯ ===============

        public Dictionary<string, object> GetSerializableData()
        {
            var result = new Dictionary<string, object>();
            var properties = GetSyncProperties(GetType());

            foreach (var prop in properties)
            {
                var syncProperty = prop.GetValue(this);
                if (syncProperty == null) continue;

                var valueProperty = syncProperty.GetType().GetProperty("Value");
                var value = valueProperty?.GetValue(syncProperty);

                if (value is SyncNode childNode)
                {
                    result[prop.Name] = childNode.GetSerializableData();
                }
                else
                {
                    result[prop.Name] = value;
                }
            }

            return result;
        }

        public void ApplySerializedData(Dictionary<string, object> data)
        {
            var properties = GetSyncProperties(GetType());

            foreach (var prop in properties)
            {
                if (!data.TryGetValue(prop.Name, out var value)) continue;

                var syncProperty = prop.GetValue(this);
                if (syncProperty == null) continue;

                if (value is Dictionary<string, object> dict)
                {
                    // Вложенный SyncNode
                    var valueProperty = syncProperty.GetType().GetProperty("Value");
                    var childNode = valueProperty?.GetValue(syncProperty) as SyncNode;
                    childNode?.ApplySerializedData(dict);
                }
                else
                {
                    // Простое значение
                    var applySnapshotMethod = syncProperty.GetType().GetMethod("ApplySnapshot");
                    applySnapshotMethod?.Invoke(syncProperty, new[] { value });
                }
            }

            SnapshotApplied?.Invoke();
        }

        // =============== ВСПОМОГАТЕЛЬНЫЕ ===============

        public void RaisePropertyChange(string propertyName, object oldValue, object newValue)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(propertyName) };
            RaiseChange(new FieldChange(path, oldValue, newValue));
        }

        protected SyncProperty<T> GetSyncProperty<T>(string propertyName)
        {
            return _syncProperties.TryGetValue(propertyName, out var obj)
                ? obj as SyncProperty<T>
                : null;
        }
    }
}