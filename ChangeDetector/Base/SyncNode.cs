using Assets.Shared.ChangeDetector.Collections;
using Assets.Shared.ChangeDetector.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Assets.Shared.ChangeDetector
{
    public abstract class SyncNode : TrackableNode
    {
        [JsonIgnore]
        public Action? Patched;

        [JsonIgnore]
        public Action? SnapshotApplied;

        [JsonIgnore]
        public Action<string, object?>? PropertyPatched;

        private readonly Dictionary<string, object> _syncProperties = new();
        private readonly Dictionary<string, SyncNode> _childNodes = new();

        private static readonly ConcurrentDictionary<Type, List<PropertyMetadata>> _propertiesCache = new();
        private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyMetadata>> _propertiesByNameCache = new();

        private class PropertyMetadata
        {
            public string Name { get; }
            public Type PropertyType { get; }
            public Type SyncPropertyType { get; }
            public bool TrackChanges { get; }
            public bool ReceivePatches { get; }
            public bool IgnoreInSnapshot { get; }
            public Func<SyncNode, object?> Getter { get; }
            public Action<SyncNode, object?>? Setter { get; }

            public PropertyMetadata(
                string name,
                Type propertyType,
                Type syncPropertyType,
                bool trackChanges,
                bool receivePatches,
                bool ignoreInSnapshot,
                Func<SyncNode, object?> getter,
                Action<SyncNode, object?>? setter)
            {
                Name = name;
                PropertyType = propertyType;
                SyncPropertyType = syncPropertyType;
                TrackChanges = trackChanges;
                ReceivePatches = receivePatches;
                IgnoreInSnapshot = ignoreInSnapshot;
                Getter = getter;
                Setter = setter;
            }
        }

        protected SyncNode()
        {
            InitializeSyncProperties();
        }

        private void InitializeSyncProperties()
        {
            var type = GetType();
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var prop in properties)
            {
                var syncFieldAttr = prop.GetCustomAttribute<SyncFieldAttribute>(inherit: true);
                if (syncFieldAttr == null)
                    continue;

                if (IsSyncProperty(prop.PropertyType))
                {
                    InitializeSyncProperty(prop, syncFieldAttr);
                }
                else if (typeof(SyncNode).IsAssignableFrom(prop.PropertyType))
                {
                    InitializeSyncNodeChild(prop, syncFieldAttr);
                }
            }
        }

        private bool IsSyncProperty(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SyncProperty<>);
        }

        private void InitializeSyncProperty(PropertyInfo prop, SyncFieldAttribute attr)
        {
            var propertyType = prop.PropertyType.GetGenericArguments()[0];
            var syncPropertyType = typeof(SyncProperty<>).MakeGenericType(propertyType);

            var getter = CreatePropertyGetter(prop);
            var setter = CreatePropertySetter(prop);

            var metadata = new PropertyMetadata(
                name: prop.Name,
                propertyType: propertyType,
                syncPropertyType: syncPropertyType,
                trackChanges: attr.TrackChanges,
                receivePatches: attr.ReceivePatches,
                ignoreInSnapshot: attr.IgnoreInSnapshot,
                getter: getter,
                setter: setter
            );

            CachePropertyMetadata(GetType(), metadata);

            var syncProperty = Activator.CreateInstance(
                syncPropertyType,
                this,
                prop.Name,
                GetDefaultValue(propertyType),
                attr.TrackChanges,
                attr.ReceivePatches
            )!;

            _syncProperties[prop.Name] = syncProperty;
            setter?.Invoke(this, syncProperty);
            SubscribeToPatchedEvents(prop.Name, syncProperty);
        }

        private void InitializeSyncNodeChild(PropertyInfo prop, SyncFieldAttribute attr)
        {
            var getter = prop.GetGetMethod(true);
            var setter = prop.GetSetMethod(true);
            if (getter == null) return;

            var childNode = getter.Invoke(this, null) as SyncNode;
            if (childNode == null && setter != null && !prop.PropertyType.IsAbstract)
            {
                try
                {
                    childNode = Activator.CreateInstance(prop.PropertyType) as SyncNode;
                    setter.Invoke(this, new[] { childNode });
                }
                catch (Exception ex)
                {
                    // Debug.LogError($"Failed to create {prop.PropertyType.Name}: {ex.Message}");
                    return;
                }
            }

            if (childNode == null) return;

            _childNodes[prop.Name] = childNode;
            childNode.Changed += CreateChildHandler(prop.Name);

            childNode.Patched += () =>
            {
                Patched?.Invoke();
                OnPropertyPatched(prop.Name, childNode);
            };
        }

        private Action<FieldChange> CreateChildHandler(string childName)
        {
            return change =>
            {
                var fullPath = new List<FieldPathSegment> { new FieldPathSegment(childName) };
                fullPath.AddRange(change.Path);
                RaiseChange(new FieldChange(fullPath, change.OldValue, change.NewValue));
            };
        }

        private void CachePropertyMetadata(Type type, PropertyMetadata metadata)
        {
            var dict = _propertiesByNameCache.GetOrAdd(type, t => new Dictionary<string, PropertyMetadata>());
            dict[metadata.Name] = metadata;

            var list = _propertiesCache.GetOrAdd(type, t => new List<PropertyMetadata>());
            list.Add(metadata);
        }

        private void SubscribeToPatchedEvents(string propertyName, object syncProperty)
        {
            var syncPropertyType = syncProperty.GetType();
            if (!syncPropertyType.IsGenericType ||
                syncPropertyType.GetGenericTypeDefinition() != typeof(SyncProperty<>))
            {
                return;
            }

            // Получаем тип значения SyncProperty<T>
            var valueType = syncPropertyType.GetGenericArguments()[0];

            // Используем generic метод для безопасной подписки
            var subscribeMethod = GetType()
                .GetMethod("SubscribeToSyncPropertyPatched", BindingFlags.NonPublic | BindingFlags.Instance);

            if (subscribeMethod != null)
            {
                try
                {
                    var genericMethod = subscribeMethod.MakeGenericMethod(valueType);
                    genericMethod.Invoke(this, new object[] { propertyName, syncProperty });
                }
                catch (Exception ex)
                {
                    // Debug.LogError($"Failed to subscribe to Patched event for {propertyName}: {ex.Message}");
                }
            }
        }

        private void SubscribeToSyncPropertyPatched<T>(string propertyName, SyncProperty<T> syncProperty)
        {
            syncProperty.Patched += value =>
            {
                Patched?.Invoke();
                OnPropertyPatched(propertyName, value);
            };
        }

        protected virtual void OnPropertyPatched(string propertyName, object? value)
        {
            PropertyPatched?.Invoke(propertyName, value);
        }

        // =============== ОСНОВНЫЕ МЕТОДЫ ===============

        /// <summary>
        /// Применяет патч по пути
        /// </summary>
        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            if (path == null || path.Count == 0)
                throw new ArgumentException("Path must be non-empty.", nameof(path));

            ApplyPatchInternal(path, 0, newValue);
        }

        private void ApplyPatchInternal(IReadOnlyList<FieldPathSegment> path, int index, object? newValue)
        {
            var segment = path[index];

            if (this is ISyncIndexableCollection collection && int.TryParse(segment.Name, out _))
            {
                ApplyPatchIntoCollection(collection, path, index, newValue);
                return;
            }

            var metadata = GetPropertyMetadata(segment.Name);
            if (metadata == null)
                throw new InvalidOperationException($"Property '{segment.Name}' not found.");

            bool isLast = index == path.Count - 1;

            if (isLast)
            {
                ApplyPatchToProperty(metadata, newValue);
                Patched?.Invoke();
            }
            else
            {
                var childValue = GetPropertyValue(metadata);

                if (childValue is SyncNode childSync)
                {
                    childSync.ApplyPatchInternal(path, index + 1, newValue);
                }
                else if (childValue is ISyncIndexableCollection childCollection)
                {
                    ApplyPatchIntoCollection(childCollection, path, index + 1, newValue);
                }
                else if (childValue == null)
                {
                    throw new InvalidOperationException(
                        $"Property '{segment.Name}' is null, cannot continue path.");
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Property '{segment.Name}' is not SyncNode or collection.");
                }
            }
        }

        private void ApplyPatchToProperty(PropertyMetadata metadata, object? newValue)
        {
            if (!metadata.ReceivePatches)
                throw new InvalidOperationException($"Property '{metadata.Name}' is not patchable.");

            var syncProperty = metadata.Getter(this);
            if (syncProperty == null) return;

            if (metadata.SyncPropertyType.IsGenericType &&
                metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                var applyPatchMethod = metadata.SyncPropertyType.GetMethod("ApplyPatch");
                if (applyPatchMethod == null) return;

                applyPatchMethod.Invoke(syncProperty, new[] {
                    newValue is Newtonsoft.Json.Linq.JToken j ? j.ToObject(metadata.PropertyType) : newValue
                });
            }
        }

        private object? GetPropertyValue(PropertyMetadata metadata)
        {
            var syncProperty = metadata.Getter(this);
            if (syncProperty == null) return null;

            if (metadata.SyncPropertyType.IsGenericType &&
                metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                var valueProperty = metadata.SyncPropertyType.GetProperty("Value");
                return valueProperty?.GetValue(syncProperty);
            }
            else if (syncProperty is SyncNode node)
            {
                return node;
            }

            return syncProperty;
        }

        private void ApplyPatchIntoCollection(
    ISyncIndexableCollection collection,
    IReadOnlyList<FieldPathSegment> path,
    int index,
    object? newValue)
        {
            if (index >= path.Count)
                throw new InvalidOperationException("Path ended before collection element.");

            var segmentName = path[index].Name;
            bool isLeaf = index == path.Count - 1;

            if (isLeaf)
            {
                // Преобразуем значение если нужно
                var convertedValue = ConvertPatchValueForCollection(collection, newValue);

                collection.SetElement(segmentName, convertedValue);
                if (collection is SyncNode syncCollection)
                {
                    syncCollection.Patched?.Invoke();
                }
            }
            else
            {
                var item = collection.GetElement(segmentName);

                if (item is SyncNode childSync)
                {
                    childSync.ApplyPatchInternal(path, index + 1, newValue);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Element '{segmentName}' of '{collection.GetType().Name}' is not SyncNode.");
                }
            }
        }

        private object? ConvertPatchValueForCollection(ISyncIndexableCollection collection, object? value)
        {
            // Для SyncList<T> пытаемся определить тип T
            if (collection is SyncList<object> syncList)
            {
                var listType = syncList.GetType();
                if (listType.IsGenericType)
                {
                    var elementType = listType.GetGenericArguments()[0];
                    return JsonPatchHelper.ConvertPatchValue(value, elementType);
                }
            }

            return value;
        }
        /// <summary>
        /// Применяет полный снапшот
        /// </summary>
        public void ApplySnapshot(SyncNode source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            ApplySnapshotInternal(source);
            SnapshotApplied?.Invoke();
        }

        private void ApplySnapshotInternal(SyncNode source)
        {
            var properties = GetPropertiesMetadataForType(GetType());

            foreach (var metadata in properties)
            {
                if (metadata.IgnoreInSnapshot) continue;

                var sourceValue = GetPropertyValue(metadata, source);
                var targetValue = GetPropertyValue(metadata, this);

                if (sourceValue == null && targetValue == null) continue;

                // 1. Обработка ISnapshotCollection
                if (targetValue is ISnapshotCollection targetCollection)
                {
                    targetCollection.ApplySnapshotFrom(sourceValue);
                }
                // 2. Обработка SyncNode
                else if (sourceValue is SyncNode childSource && targetValue is SyncNode childTarget)
                {
                    childTarget.ApplySnapshotInternal(childSource);
                }
                // 3. Обработка SyncProperty<T>
                else if (metadata.SyncPropertyType.IsGenericType &&
                        metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
                {
                    var syncPropertyTarget = metadata.Getter(this);
                    if (syncPropertyTarget != null)
                    {
                        var applySnapshotMethod = metadata.SyncPropertyType.GetMethod("ApplySnapshot");
                        applySnapshotMethod?.Invoke(syncPropertyTarget, new[] { sourceValue });
                    }
                }
                // 4. Простые значения через сеттер
                else
                {
                    metadata.Setter?.Invoke(this, sourceValue);
                }
            }
        }

        private static object? GetPropertyValue(PropertyMetadata metadata, SyncNode instance)
        {
            var syncProperty = metadata.Getter(instance);
            if (syncProperty == null) return null;

            if (metadata.SyncPropertyType.IsGenericType &&
                metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                var valueProperty = metadata.SyncPropertyType.GetProperty("Value");
                return valueProperty?.GetValue(syncProperty);
            }
            else if (syncProperty is SyncNode node)
            {
                return node;
            }

            return syncProperty;
        }

        // =============== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ===============

        public void RaisePropertyChange(string propertyName, object? oldValue, object? newValue)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(propertyName) };
            RaiseChange(new FieldChange(path, oldValue, newValue));
        }

        private PropertyMetadata? GetPropertyMetadata(string propertyName)
        {
            var dict = _propertiesByNameCache.GetOrAdd(GetType(), type =>
                GetPropertiesMetadataForType(type).ToDictionary(m => m.Name));

            return dict.TryGetValue(propertyName, out var metadata) ? metadata : null;
        }

        private static List<PropertyMetadata> GetPropertiesMetadataForType(Type type)
        {
            return _propertiesCache.GetOrAdd(type, t =>
            {
                var result = new List<PropertyMetadata>();
                var properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (var prop in properties)
                {
                    var syncFieldAttr = prop.GetCustomAttribute<SyncFieldAttribute>(inherit: true);
                    if (syncFieldAttr == null) continue;

                    Type? propertyType = null;
                    Type? syncPropertyType = null;

                    if (prop.PropertyType.IsGenericType &&
                        prop.PropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
                    {
                        propertyType = prop.PropertyType.GetGenericArguments()[0];
                        syncPropertyType = typeof(SyncProperty<>).MakeGenericType(propertyType);
                    }
                    else if (typeof(SyncNode).IsAssignableFrom(prop.PropertyType))
                    {
                        propertyType = prop.PropertyType;
                        syncPropertyType = prop.PropertyType;
                    }
                    else
                    {
                        continue;
                    }

                    var getter = CreatePropertyGetter(prop);
                    var setter = CreatePropertySetter(prop);

                    result.Add(new PropertyMetadata(
                        name: prop.Name,
                        propertyType: propertyType!,
                        syncPropertyType: syncPropertyType!,
                        trackChanges: syncFieldAttr.TrackChanges,
                        receivePatches: syncFieldAttr.ReceivePatches,
                        ignoreInSnapshot: syncFieldAttr.IgnoreInSnapshot,
                        getter: getter,
                        setter: setter
                    ));
                }

                return result;
            });
        }

        private static Func<SyncNode, object?> CreatePropertyGetter(PropertyInfo prop)
        {
            var getMethod = prop.GetGetMethod(true);
            if (getMethod == null) return _ => null;

            return obj => getMethod.Invoke(obj, Array.Empty<object>());
        }

        private static Action<SyncNode, object?>? CreatePropertySetter(PropertyInfo prop)
        {
            var setMethod = prop.GetSetMethod(true);
            if (setMethod == null) return null;

            return (obj, value) => setMethod.Invoke(obj, new[] { value });
        }

        private static object? GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        /// <summary>
        /// Получает SyncProperty
        /// </summary>
        protected SyncProperty<T>? GetSyncProperty<T>(string propertyName)
        {
            return _syncProperties.TryGetValue(propertyName, out var obj)
                ? obj as SyncProperty<T>
                : null;
        }

        protected override void RaiseChange(FieldChange change)
        {
            // Debug.Log($"[SyncNode {GetType().Name}] Change: {string.Join(".", change.Path)} = {change.NewValue}");
            base.RaiseChange(change);
        }

        /// <summary>
        /// Получает данные для сериализации
        /// </summary>
        public Dictionary<string, object?> GetSerializableData()
        {
            var result = new Dictionary<string, object?>();
            var properties = GetPropertiesMetadataForType(GetType());

            foreach (var metadata in properties)
            {
                if (metadata.IgnoreInSnapshot) continue;

                var value = GetPropertyValue(metadata, this);
                result[metadata.Name] = value;
            }

            return result;
        }

        /// <summary>
        /// Восстанавливает данные из словаря
        /// </summary>
        public void ApplySerializedData(Dictionary<string, object?> data)
        {
            var properties = GetPropertiesMetadataForType(GetType());

            foreach (var metadata in properties)
            {
                if (!data.TryGetValue(metadata.Name, out var value)) continue;

                ApplySerializedValue(metadata, value);
            }
        }

        private void ApplySerializedValue(PropertyMetadata metadata, object? value)
        {
            var syncProperty = metadata.Getter(this);

            if (metadata.SyncPropertyType.IsGenericType &&
                metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                var syncProp = metadata.Getter(this);
                if (syncProp != null)
                {
                    var applySnapshotMethod = metadata.SyncPropertyType.GetMethod("ApplySnapshot");
                    applySnapshotMethod?.Invoke(syncProp, new[] { value });
                }
            }
            else if (syncProperty is System.Collections.IList list && value is System.Collections.IEnumerable enumerable)
            {
                list.Clear();
                foreach (var item in enumerable)
                {
                    list.Add(item);
                }
            }
            else if (syncProperty is SyncNode node && value is Dictionary<string, object?> nodeData)
            {
                node.ApplySerializedData(nodeData);
            }
            else if (metadata.Setter != null)
            {
                metadata.Setter(this, value);
            }
        }
    }
}