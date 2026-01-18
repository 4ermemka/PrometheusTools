using Assets.Shared.ChangeDetector.Base;
using Assets.Shared.ChangeDetector.Collections;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Assets.Shared.ChangeDetector.Base
{
    /// <summary>
    /// Атрибут для автоматической регистрации SyncProperty
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class SyncFieldAttribute : Attribute
    {
        public bool TrackChanges { get; set; } = true;
        public bool ReceivePatches { get; set; } = true;
        public object? DefaultValue { get; set; }
    }

    /// <summary>
    /// Улучшенный SyncNode с автоматическим управлением SyncProperty
    /// </summary>
    public abstract class SyncNode : TrackableNode
    {
        [JsonIgnore]
        public Action<string, object?>? PropertyPatched; // Имя свойства + новое значение

        [JsonIgnore]
        public Action? SnapshotApplied;

        // Регистрация всех SyncProperty
        private readonly Dictionary<string, object> _syncProperties = new();
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _syncPropertiesCache = new();

        protected SyncNode()
        {
            RegisterSyncProperties();
        }

        /// <summary>
        ///Автоматическая регистрация всех свойств с[SyncField]
        /// </summary>
        private void RegisterSyncProperties()
        {
            var type = GetType();
            var properties = GetSyncPropertiesForType(type);

            foreach (var prop in properties)
            {
                var attr = prop.GetCustomAttribute<SyncFieldAttribute>(inherit: true)!;

                // Создаем экземпляр SyncProperty<T>
                var syncPropertyType = typeof(SyncProperty<>).MakeGenericType(prop.PropertyType);
                var defaultValue = attr.DefaultValue ?? GetDefaultValue(prop.PropertyType);

                var syncProperty = Activator.CreateInstance(
                    syncPropertyType,
                    this,
                    prop.Name,
                    defaultValue,
                    attr.TrackChanges,
                    attr.ReceivePatches
                )!;

                _syncProperties[prop.Name] = syncProperty;

                // Подписываемся на Patched события
                var patchedEvent = syncPropertyType.GetEvent("Patched");
                if (patchedEvent != null)
                {
                    var handler = CreatePatchedHandler(prop.Name);
                    patchedEvent.AddEventHandler(syncProperty, handler);
                }

                // Устанавливаем значение в свойство (через бэкинг-поле)
                SetBackingField(prop, syncProperty);
            }
        }

        private Delegate CreatePatchedHandler(string propertyName)
        {
            // Создаем делегат для вызова PropertyPatched
            return new Action<object?>(value =>
            {
                PropertyPatched?.Invoke(propertyName, value);
            });
        }

        private void SetBackingField(PropertyInfo property, object syncProperty)
        {
            // Ищем бэкинг-поле (обычно имеет имя _propertyName)
            var backingFieldName = $"<{property.Name}>k__BackingField"; // Для автоматических свойств
            var field = GetType().GetField(backingFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field != null && field.FieldType == property.PropertyType)
            {
                field.SetValue(this, syncProperty);
            }
        }

        private static PropertyInfo[] GetSyncPropertiesForType(Type type)
        {
            return _syncPropertiesCache.GetOrAdd(type, t =>
                t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                 .Where(p => p.GetCustomAttribute<SyncFieldAttribute>(inherit: true) != null)
                 .ToArray()
            );
        }

        private static object? GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        /// <summary>
        /// Применение патча из сети
        /// </summary>
        public void ApplyPatch(string propertyName, object? newValue)
        {
            if (!_syncProperties.TryGetValue(propertyName, out var syncPropertyObj))
                throw new InvalidOperationException($"Property '{propertyName}' not found.");

            var syncPropertyType = syncPropertyObj.GetType();
            var applyPatchMethod = syncPropertyType.GetMethod("ApplyPatch");

            if (applyPatchMethod == null)
                throw new InvalidOperationException($"Property '{propertyName}' is not patchable.");

            // Конвертируем значение если нужно
            var valueType = syncPropertyType.GetGenericArguments()[0];
            var convertedValue = ConvertIfNeeded(newValue, valueType);

            applyPatchMethod.Invoke(syncPropertyObj, new[] { convertedValue });
        }

        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            if (path == null || path.Count == 0)
                throw new ArgumentException("Path must be non-empty.");

            // Для простоты - первый сегмент это имя свойства
            if (path.Count == 1)
            {
                ApplyPatch(path[0].Name, newValue);
                return;
            }

            // Для вложенных путей - рекурсия
            ApplyPatchInternal(path, 0, newValue);
        }

        private void ApplyPatchInternal(IReadOnlyList<FieldPathSegment> path, int index, object? newValue)
        {
            // Реализация для вложенных объектов...
            // (аналогично предыдущей версии, но работающая с SyncProperty)
        }

        /// <summary>
        /// Поднимает изменение свойства для отправки в сеть
        /// </summary>
        public void RaisePropertyChange(string propertyName, object? oldValue, object? newValue)
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(propertyName) };
            RaiseChange(new FieldChange(path, oldValue, newValue));
        }

        protected object? ConvertIfNeeded(object? value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType.IsInstanceOfType(value))
                return value;

            return Convert.ChangeType(value, targetType);
        }

        // Для удобства - метод получения SyncProperty
        protected SyncProperty<T>? GetSyncProperty<T>(string propertyName)
        {
            return _syncProperties.TryGetValue(propertyName, out var obj)
                ? obj as SyncProperty<T>
                : null;
        }
    }
}