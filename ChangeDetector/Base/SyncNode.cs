using Assets.Shared.ChangeDetector.Collections;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Узел, который умеет применять входящие патчи и снапшоты.
    /// При этом не поднимает Changed/FieldChange и не формирует исходящие патчи.
    /// </summary>
    public abstract class SyncNode : TrackableNode
    {
        [JsonIgnore]
        public Action? Patched;

        [JsonIgnore]
        public Action? SnapshotApplied;

        // Хранилище SyncProperty объектов
        private readonly Dictionary<string, object> _syncProperties = new();

        // Кэш метаданных свойств
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
        /// Инициализирует все SyncProperty атрибутами
        /// </summary>
        public void InitializeSyncProperties()
        {
            var type = GetType();
            var metadataList = GetPropertiesMetadataForType(type);

            foreach (var metadata in metadataList)
            {
                // Создаем экземпляр SyncProperty<T>
                var syncProperty = Activator.CreateInstance(
                    metadata.SyncPropertyType,
                    this,
                    metadata.Name,
                    GetDefaultValue(metadata.PropertyType),
                    metadata.TrackChanges,
                    metadata.ReceivePatches
                )!;

                _syncProperties[metadata.Name] = syncProperty;

                // Устанавливаем значение в бэкинг-поле свойства
                metadata.Setter?.Invoke(this, syncProperty);

                // Подписываемся на события Patched
                SubscribeToPatchedEvents(metadata.Name, syncProperty);
            }
        }

        /// <summary>
        /// Подписывается на события Patched SyncProperty
        /// </summary>
        private void SubscribeToPatchedEvents(string propertyName, object syncProperty)
        {
            if (syncProperty is SyncNode syncNode)
            {
                // Для любого SyncNode (включая SyncList)
                syncNode.Patched += () =>
                {
                    Patched?.Invoke();
                    OnPropertyPatched(propertyName, syncProperty);
                };
            }
            else
            {
                // Для SyncProperty<T> - старая логика
                var patchedEvent = syncProperty.GetType().GetEvent("Patched");
                if (patchedEvent != null)
                {
                    // Упрощенная версия - подписываемся только если можем
                    try
                    {
                        var handler = CreatePatchedHandler(propertyName);
                        patchedEvent.AddEventHandler(syncProperty, handler);
                    }
                    catch
                    {
                        // Игнорируем если не удалось подписаться
                    }
                }
            }
        }

        private Delegate CreatePatchedHandler(string propertyName)
        {
            return new Action<object?>(value =>
            {
                Patched?.Invoke();
                OnPropertyPatched(propertyName, value);
            });
        }

        /// <summary>
        /// Вызывается при патче конкретного свойства
        /// </summary>
        protected virtual void OnPropertyPatched(string propertyName, object? value)
        {
            // Можно переопределить в наследниках
        }

        /// <summary>
        /// Применяет патч по пути
        /// </summary>
        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            if (path == null || path.Count == 0)
                throw new ArgumentException("Path must be non-empty.", nameof(path));

            ApplyPatchInternal(path, 0, newValue);
        }

        /// <summary>
        /// Рекурсивное применение патча с поддержкой коллекций
        /// </summary>
        private void ApplyPatchInternal(IReadOnlyList<FieldPathSegment> path, int index, object? newValue)
        {
            var segment = path[index];

            // Если текущий объект - коллекция
            if (this is ISyncIndexableCollection collection && PathHelper.IsCollectionIndex(segment.Name))
            {
                ApplyPatchIntoCollection(collection, path, index, newValue);
                return;
            }

            // Ищем обычное свойство
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
                ContinuePatch(childValue, path, index + 1, newValue);
            }
        }

        /// <summary>
        /// Применяет патч к конкретному свойству
        /// </summary>
        private void ApplyPatchToProperty(PropertyMetadata metadata, object? newValue)
        {
            if (!metadata.ReceivePatches)
                throw new InvalidOperationException($"Property '{metadata.Name}' is not patchable.");

            var syncProperty = metadata.Getter(this);
            if (syncProperty == null)
                return;

            var applyPatchMethod = metadata.SyncPropertyType.GetMethod("ApplyPatch");
            if (applyPatchMethod == null)
                return;

            var convertedValue = ConvertIfNeeded(newValue, metadata.PropertyType);
            applyPatchMethod.Invoke(syncProperty, new[] { convertedValue });
        }

        /// <summary>
        /// Получает значение свойства
        /// </summary>
        private object? GetPropertyValue(PropertyMetadata metadata)
        {
            var syncProperty = metadata.Getter(this);
            if (syncProperty == null)
                return null;

            var valueProperty = metadata.SyncPropertyType.GetProperty("Value");
            return valueProperty?.GetValue(syncProperty);
        }

        /// <summary>
        /// Продолжает путь патча
        /// </summary>
        private void ContinuePatch(object? childValue, IReadOnlyList<FieldPathSegment> path, int index, object? newValue)
        {
            if (childValue is ISyncIndexableCollection collection)
            {
                ApplyPatchIntoCollection(collection, path, index, newValue);
            }
            else if (childValue is SyncNode childSync)
            {
                childSync.ApplyPatchInternal(path, index, newValue);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Cannot continue path: value is not SyncNode or collection.");
            }
        }

        /// <summary>
        /// Применение патча внутрь коллекции
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
        /// Применяет полный снапшот
        /// </summary>
        public void ApplySnapshot(SyncNode source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (source.GetType() != GetType())
                throw new InvalidOperationException(
                    $"ApplySnapshot type mismatch: source={source.GetType().Name}, target={GetType().Name}");

            var path = new List<FieldPathSegment>();
            ApplySnapshotRecursive(root: this, target: this, source: source, path);
            SnapshotApplied?.Invoke();
        }

        /// <summary>
        /// Рекурсивное применение снапшота
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

                var syncPropertySource = metadata.Getter(source);
                var syncPropertyTarget = metadata.Getter(target);

                if (syncPropertySource == null || syncPropertyTarget == null)
                    continue;

                path.Add(new FieldPathSegment(metadata.Name));

                // Получаем значения из SyncProperty
                var getValueMethod = metadata.SyncPropertyType.GetProperty("Value")?.GetGetMethod();
                var setValueMethod = metadata.SyncPropertyType.GetProperty("Value")?.GetSetMethod();

                if (getValueMethod != null && setValueMethod != null)
                {
                    var sourceValue = getValueMethod.Invoke(syncPropertySource, null);
                    var targetValue = getValueMethod.Invoke(syncPropertyTarget, null);

                    if (typeof(ISnapshotCollection).IsAssignableFrom(metadata.PropertyType) &&
                        targetValue is ISnapshotCollection targetCollection)
                    {
                        targetCollection.ApplySnapshotFrom(sourceValue);
                    }
                    else if (typeof(SyncNode).IsAssignableFrom(metadata.PropertyType) &&
                             sourceValue is SyncNode childSource &&
                             targetValue is SyncNode childTarget)
                    {
                        ApplySnapshotRecursive(root, childTarget, childSource, path);
                    }
                    else
                    {
                        // Применяем значение напрямую через сеттер SyncProperty
                        setValueMethod.Invoke(syncPropertyTarget, new[] { sourceValue });
                    }
                }

                path.RemoveAt(path.Count - 1);
            }
        }

        /// <summary>
        /// Генерирует изменение свойства для отправки в сеть
        /// </summary>
        public void RaisePropertyChange(string propertyName, object? oldValue, object? newValue)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(propertyName) };
            RaiseChange(new FieldChange(path, oldValue, newValue));
        }

        /// <summary>
        /// Получает метаданные свойства по имени
        /// </summary>
        private PropertyMetadata? GetPropertyMetadata(string propertyName)
        {
            var dict = _propertiesByNameCache.GetOrAdd(GetType(), type =>
                GetPropertiesMetadataForType(type).ToDictionary(m => m.Name));

            return dict.TryGetValue(propertyName, out var metadata) ? metadata : null;
        }

        /// <summary>
        /// Получает метаданные всех свойств с атрибутом SyncField
        /// </summary>
        private static List<PropertyMetadata> GetPropertiesMetadataForType(Type type)
        {
            return _propertiesCache.GetOrAdd(type, t =>
            {
                var result = new List<PropertyMetadata>();
                var properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (var prop in properties)
                {
                    // Проверяем, что свойство имеет тип SyncProperty<T>
                    if (!prop.PropertyType.IsGenericType ||
                        prop.PropertyType.GetGenericTypeDefinition() != typeof(SyncProperty<>))
                        continue;

                    // Получаем атрибут
                    var attr = prop.GetCustomAttribute<SyncFieldAttribute>(inherit: true);
                    if (attr == null)
                        continue;

                    var propertyType = prop.PropertyType.GetGenericArguments()[0];
                    var syncPropertyType = typeof(SyncProperty<>).MakeGenericType(propertyType);

                    // Создаем делегаты для доступа к свойству
                    var getter = CreatePropertyGetter(prop);
                    var setter = CreatePropertySetter(prop);

                    result.Add(new PropertyMetadata(
                        name: prop.Name,
                        propertyType: propertyType,
                        syncPropertyType: syncPropertyType,
                        trackChanges: attr.TrackChanges,
                        receivePatches: attr.ReceivePatches,
                        ignoreInSnapshot: attr.IgnoreInSnapshot,
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
        /// Получает SyncProperty для чтения/записи (удобный метод для наследников)
        /// </summary>
        protected SyncProperty<T>? GetSyncProperty<T>(string propertyName)
        {
            return _syncProperties.TryGetValue(propertyName, out var obj)
                ? obj as SyncProperty<T>
                : null;
        }
    }
}