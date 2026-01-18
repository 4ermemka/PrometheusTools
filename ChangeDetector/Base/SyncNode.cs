using Assets.Shared.ChangeDetector.Base.Mapping;
using Assets.Shared.ChangeDetector.Collections;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

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

        /// <summary>
        /// Инициализирует все трекабельные свойства.
        /// </summary>
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
            return type.IsGenericType &&
                   type.GetGenericTypeDefinition() == typeof(SyncProperty<>);
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
                    //Debug.LogError($"Failed to create {prop.PropertyType.Name}: {ex.Message}");
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
            if (syncProperty is SyncNode syncNode)
            {
                syncNode.Patched += () =>
                {
                    Patched?.Invoke();
                    OnPropertyPatched(propertyName, syncProperty);
                };
            }
            else
            {
                var patchedEvent = syncProperty.GetType().GetEvent("Patched");
                if (patchedEvent != null)
                {
                    try
                    {
                        var handler = CreateGenericPatchedHandler(propertyName, syncProperty.GetType());
                        patchedEvent.AddEventHandler(syncProperty, handler);
                    }
                    catch (Exception ex)
                    {
                        //Debug.LogWarning($"Failed to subscribe to Patched: {ex.Message}");
                    }
                }
            }
        }

        private Delegate CreateGenericPatchedHandler(string propertyName, Type syncPropertyType)
        {
            var genericArg = syncPropertyType.GetGenericArguments()[0];
            var method = GetType().GetMethod("CreatePatchedHandlerGeneric",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var genericMethod = method!.MakeGenericMethod(genericArg);
            return (Delegate)genericMethod.Invoke(this, new object[] { propertyName });
        }

        protected virtual void OnPropertyPatched(string propertyName, object? value)
        {
            PropertyPatched?.Invoke(propertyName, value);
        }

        // =============== КРИТИЧЕСКИ ВАЖНЫЕ МЕТОДЫ ===============

        /// <summary>
        /// Применяет патч по пути.
        /// </summary>
        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            if (path == null || path.Count == 0)
                throw new ArgumentException("Path must be non-empty.", nameof(path));

            ApplyPatchInternal(path, 0, newValue);
        }

        /// <summary>
        /// Рекурсивное применение патча.
        /// </summary>
        private void ApplyPatchInternal(IReadOnlyList<FieldPathSegment> path, int index, object? newValue)
        {
            var segment = path[index];

            // Проверяем, является ли текущий объект коллекцией
            if (this is ISyncIndexableCollection collection && PathHelper.IsCollectionIndex(segment.Name))
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
                // Конечный путь: устанавливаем значение
                ApplyPatchToProperty(metadata, newValue);
                Patched?.Invoke();
            }
            else
            {
                // Продолжаем путь вглубь
                var childValue = GetPropertyValue(metadata);

                // Если childValue - это SyncNode, продолжаем путь в нем
                if (childValue is SyncNode childSync)
                {
                    childSync.ApplyPatchInternal(path, index + 1, newValue);
                }
                // Если это коллекция
                else if (childValue is ISyncIndexableCollection syncIndexablecollection)
                {
                    ApplyPatchIntoCollection(syncIndexablecollection, path, index + 1, newValue);
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
        
        /// <summary>
         /// Применяет патч к свойству.
         /// </summary>
        private void ApplyPatchToProperty(PropertyMetadata metadata, object? newValue)
        {
            if (!metadata.ReceivePatches)
                throw new InvalidOperationException($"Property '{metadata.Name}' is not patchable.");

            var syncProperty = metadata.Getter(this);
            if (syncProperty == null)
                return;

            // Если это SyncProperty<T> - вызываем ApplyPatch
            if (metadata.SyncPropertyType.IsGenericType &&
                metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                var applyPatchMethod = metadata.SyncPropertyType.GetMethod("ApplyPatch");
                if (applyPatchMethod == null)
                    return;

                var convertedValue = ConvertIfNeeded(newValue, metadata.PropertyType);
                applyPatchMethod.Invoke(syncProperty, new[] { convertedValue });
            }
            // Если это SyncNode (например, SyncList) - делегируем дальше
            else if (syncProperty is SyncNode node)
            {
                // Для SyncNode нельзя применить патч напрямую
                // Нужно продолжать путь, но это обрабатывается в ApplyPatchInternal
                throw new InvalidOperationException($"Cannot apply patch directly to SyncNode property '{metadata.Name}'");
            }
        }

        /// <summary>
        /// Получает значение свойства.
        /// </summary>
        private object? GetPropertyValue(PropertyMetadata metadata)
        {
            var syncProperty = metadata.Getter(this);
            if (syncProperty == null)
                return null;

            // Для SyncProperty<T> - возвращаем Value
            if (metadata.SyncPropertyType.IsGenericType &&
                metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                var valueProperty = metadata.SyncPropertyType.GetProperty("Value");
                return valueProperty?.GetValue(syncProperty);
            }
            // Для SyncNode - возвращаем сам объект
            else if (syncProperty is SyncNode node)
            {
                return node;
            }

            return syncProperty;
        }

        /// <summary>
        /// Применение патча внутрь коллекции.
        /// </summary>
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
                collection.SetElement(segmentName, newValue);
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

        /// <summary>
        /// Применяет полный снапшот.
        /// </summary>
        public void ApplySnapshot(SyncNode source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            // Конвертируем source в DTO, затем применяем к target
            var serializedData = source.GetSerializableData();
            ApplySerializedData(serializedData);
            SnapshotApplied?.Invoke();
        }

        /// <summary>
        /// Рекурсивное применение снапшота.
        /// </summary>
        private static void ApplySnapshotRecursive(
            SyncNode root,
            SyncNode target,
            SyncNode source,
            List<FieldPathSegment> path)
        {
            var properties = GetPropertiesMetadataForType(target.GetType());

            foreach (var metadata in properties)
            {
                if (metadata.IgnoreInSnapshot)
                    continue;

                var sourceValue = GetPropertyValue(metadata, source);
                var targetValue = GetPropertyValue(metadata, target);

                if (sourceValue == null && targetValue == null)
                    continue;

                path.Add(new FieldPathSegment(metadata.Name));

                // 1. Обработка ISnapshotCollection (SyncList, SyncDictionary)
                if (targetValue is ISnapshotCollection targetCollection)
                {
                    targetCollection.ApplySnapshotFrom(sourceValue);
                }
                // 2. Обработка SyncNode (вложенные объекты)
                else if (sourceValue is SyncNode childSource &&
                         targetValue is SyncNode childTarget)
                {
                    ApplySnapshotRecursive(root, childTarget, childSource, path);
                }
                // 3. Обработка SyncProperty<T> и простых значений
                else
                {
                    // Для SyncProperty<T> - вызываем ApplySnapshot
                    if (metadata.SyncPropertyType.IsGenericType &&
                        metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
                    {
                        var syncPropertyTarget = metadata.Getter(target);
                        if (syncPropertyTarget != null)
                        {
                            var applySnapshotMethod = metadata.SyncPropertyType.GetMethod("ApplySnapshot");
                            if (applySnapshotMethod != null)
                            {
                                applySnapshotMethod.Invoke(syncPropertyTarget, new[] { sourceValue });
                            }
                        }
                    }
                    // Для простых значений - через SetProperty
                    else
                    {
                        // Если свойство имеет сеттер - используем его
                        metadata.Setter?.Invoke(target, sourceValue);
                    }
                }

                path.RemoveAt(path.Count - 1);
            }
        }

        // Вспомогательный метод для получения значения свойства
        private static object? GetPropertyValue(PropertyMetadata metadata, SyncNode instance)
        {
            var syncProperty = metadata.Getter(instance);
            if (syncProperty == null)
                return null;

            // Для SyncProperty<T> - возвращаем Value
            if (metadata.SyncPropertyType.IsGenericType &&
                metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                var valueProperty = metadata.SyncPropertyType.GetProperty("Value");
                return valueProperty?.GetValue(syncProperty);
            }
            // Для SyncNode - возвращаем сам объект
            else if (syncProperty is SyncNode node)
            {
                return node;
            }

            return syncProperty;
        }

        // =============== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ===============

        /// <summary>
        /// Генерирует изменение свойства.
        /// </summary>
        public void RaisePropertyChange(string propertyName, object? oldValue, object? newValue)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(propertyName) };
            RaiseChange(new FieldChange(path, oldValue, newValue));
        }

        /// <summary>
        /// Получает метаданные свойства.
        /// </summary>
        private PropertyMetadata? GetPropertyMetadata(string propertyName)
        {
            var dict = _propertiesByNameCache.GetOrAdd(GetType(), type =>
                GetPropertiesMetadataForType(type).ToDictionary(m => m.Name));

            return dict.TryGetValue(propertyName, out var metadata) ? metadata : null;
        }

        /// <summary>
        /// Получает все метаданные свойств.
        /// </summary>
        private static List<PropertyMetadata> GetPropertiesMetadataForType(Type type)
        {
            return _propertiesCache.GetOrAdd(type, t =>
            {
                var result = new List<PropertyMetadata>();
                var properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (var prop in properties)
                {
                    var syncFieldAttr = prop.GetCustomAttribute<SyncFieldAttribute>(inherit: true);
                    if (syncFieldAttr == null)
                        continue;

                    Type? propertyType = null;
                    Type? syncPropertyType = null;

                    // 1. SyncProperty<T>
                    if (prop.PropertyType.IsGenericType &&
                        prop.PropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
                    {
                        propertyType = prop.PropertyType.GetGenericArguments()[0];
                        syncPropertyType = typeof(SyncProperty<>).MakeGenericType(propertyType);
                    }
                    // 2. SyncNode (включая SyncList<T>)
                    else if (typeof(SyncNode).IsAssignableFrom(prop.PropertyType))
                    {
                        propertyType = prop.PropertyType;
                        syncPropertyType = prop.PropertyType; // Для SyncNode syncPropertyType = сам тип
                    }
                    else
                    {
                        continue; // Неподдерживаемый тип
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
            if (getMethod == null)
                return _ => null;

            return obj => getMethod.Invoke(obj, Array.Empty<object>());
        }

        private static Action<SyncNode, object?>? CreatePropertySetter(PropertyInfo prop)
        {
            var setMethod = prop.GetSetMethod(true);
            if (setMethod == null)
                return null;

            return (obj, value) => setMethod.Invoke(obj, new[] { value });
        }

        private static object? GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        protected object? ConvertIfNeeded(object? value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType.IsInstanceOfType(value))
                return value;

            return Convert.ChangeType(value, targetType);
        }

        /// <summary>
        /// Получает SyncProperty.
        /// </summary>
        protected SyncProperty<T>? GetSyncProperty<T>(string propertyName)
        {
            return _syncProperties.TryGetValue(propertyName, out var obj)
                ? obj as SyncProperty<T>
                : null;
        }

        /// <summary>
        /// Для отладки - переопределите чтобы видеть все изменения.
        /// </summary>
        protected override void RaiseChange(FieldChange change)
        {
            //Debug.Log($"[SyncNode {GetType().Name}] Change: {string.Join(".", change.Path)} = {change.NewValue}");
            base.RaiseChange(change);
        }

        public Dictionary<string, object?> GetSerializableData()
        {
            var result = new Dictionary<string, object?>();
            var properties = GetPropertiesMetadataForType(GetType());

            foreach (var metadata in properties)
            {
                var value = GetPropertyValueForSerialization(metadata);
                result[metadata.Name] = SyncValueConverter.ToDtoIfNeeded(value);
            }

            return result;
        }

        private object? GetPropertyValueForSerialization(PropertyMetadata metadata)
        {
            var syncProperty = metadata.Getter(this);
            if (syncProperty == null)
                return null;

            // Для SyncProperty<T> - возвращаем Value
            if (metadata.SyncPropertyType.IsGenericType &&
                metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                var valueProperty = metadata.SyncPropertyType.GetProperty("Value");
                return valueProperty?.GetValue(syncProperty);
            }
            // Для SyncList<T> - сериализуем как массив
            else if (syncProperty is IEnumerable enumerable)
            {
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    if (item is SyncNode node)
                    {
                        list.Add(node.GetSerializableData());
                    }
                    else
                    {
                        list.Add(SyncValueConverter.ToDtoIfNeeded(item));
                    }
                }
                return list;
            }
            // Для вложенного SyncNode
            else if (syncProperty is SyncNode node)
            {
                return node.GetSerializableData();
            }

            return syncProperty;
        }

        /// <summary>
        /// Восстанавливает данные из сериализованного словаря
        /// </summary>
        public void ApplySerializedData(Dictionary<string, object?> data)
        {
            var properties = GetPropertiesMetadataForType(GetType());

            foreach (var metadata in properties)
            {
                if (!data.TryGetValue(metadata.Name, out var serializedValue))
                    continue;

                ApplySerializedValue(metadata, serializedValue);
            }
        }

        private void ApplySerializedValue(PropertyMetadata metadata, object? serializedValue)
        {
            var syncProperty = metadata.Getter(this);
            if (syncProperty == null)
                return;

            object? value = SyncValueConverter.FromDtoIfNeeded(serializedValue);

            // Для SyncProperty<T> - устанавливаем Value
            if (metadata.SyncPropertyType.IsGenericType &&
                metadata.SyncPropertyType.GetGenericTypeDefinition() == typeof(SyncProperty<>))
            {
                var valueProperty = metadata.SyncPropertyType.GetProperty("Value");
                valueProperty?.SetValue(syncProperty, value);
            }
            // Для SyncList<T> - очищаем и добавляем элементы
            else if (syncProperty is IList list && value is IEnumerable enumerable)
            {
                list.Clear();
                foreach (var item in enumerable)
                {
                    if (item is Dictionary<string, object?> itemData)
                    {
                        // Создаем новый элемент и применяем данные
                        var itemType = metadata.PropertyType.GetGenericArguments()[0];
                        if (typeof(SyncNode).IsAssignableFrom(itemType))
                        {
                            var newItem = (SyncNode)Activator.CreateInstance(itemType);
                            newItem.ApplySerializedData(itemData);
                            list.Add(newItem);
                        }
                    }
                    else
                    {
                        list.Add(item);
                    }
                }
            }
            // Для вложенного SyncNode
            else if (syncProperty is SyncNode node && value is Dictionary<string, object?> nodeData)
            {
                node.ApplySerializedData(nodeData);
            }
        }
    }
}