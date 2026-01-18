using Assets.Shared.ChangeDetector.Base.Mapping;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Базовый класс для всех синхронизируемых объектов.
    /// Отслеживает изменения полей/свойств с [Track] и автоматически подписывается
    /// на вложенные TrackableNode.
    /// </summary>
    public abstract class TrackableNode
    {
        /// <summary>
        /// Событие изменения любого отслеживаемого поля в этом объекте или во вложенных TrackableNode.
        /// </summary>
        public event Action<FieldChange>? Changed;

        // Карта: имя члена -> является ли он [Track] (для обратной совместимости)
        private readonly Dictionary<string, bool> _trackMap = new();

        // Карта: имя дочернего члена -> TrackableNode (для bubbling)
        private readonly Dictionary<string, TrackableNode?> _childNodes = new();

        // Кэш членов для каждого типа (локальный для экземпляра)
        private static readonly ConcurrentDictionary<Type, List<MemberAccessor>> _trackedMembersCache = new();

        /// <summary>
        /// Внутренний класс для хранения информации о члене с делегатами доступа
        /// </summary>
        private class MemberAccessor
        {
            public string Name { get; }
            public Type MemberType { get; }
            public Func<object, object?> Getter { get; }
            public Action<object, object?>? Setter { get; }
            public bool AutoCreateChildren { get; }
            public bool WireChildren { get; }
            public TrackAttribute? TrackAttribute { get; }

            public MemberAccessor(
                string name,
                Type memberType,
                Func<object, object?> getter,
                Action<object, object?>? setter,
                TrackAttribute? trackAttribute)
            {
                Name = name;
                MemberType = memberType;
                Getter = getter;
                Setter = setter;
                TrackAttribute = trackAttribute;
                AutoCreateChildren = trackAttribute?.AutoCreate ?? true;
                WireChildren = trackAttribute?.WireChildren ?? true;
            }
        }

        protected TrackableNode()
        {
            BuildTrackMapAndWireChildren();
        }

        /// <summary>
        /// Получает отслеживаемые члены для типа с кэшированием
        /// </summary>
        private static List<MemberAccessor> GetTrackedMembersForType(Type type)
        {
            return _trackedMembersCache.GetOrAdd(type, t =>
            {
                var members = new List<MemberAccessor>();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var member in t.GetMembers(flags))
                {
                    if (member is not FieldInfo && member is not PropertyInfo)
                        continue;

                    // Проверяем атрибут [Track]
                    var trackAttr = member.GetCustomAttribute<TrackAttribute>(inherit: true);
                    if (trackAttr == null)
                        continue;

                    // Для свойств проверяем доступность
                    if (member is PropertyInfo pi)
                    {
                        if (pi.GetIndexParameters().Length > 0)
                            continue; // Индексаторы

                        if (pi.GetGetMethod(true) == null)
                            continue; // Нет геттера

                        // Для Track-атрибута проверяем сеттер
                        if (pi.GetSetMethod(true) == null)
                            continue;
                    }

                    // Создаем делегаты для доступа
                    var getter = CreateGetter(member);
                    var setter = CreateSetter(member);
                    var memberType = GetMemberType(member);

                    members.Add(new MemberAccessor(
                        name: member.Name,
                        memberType: memberType,
                        getter: getter,
                        setter: setter,
                        trackAttribute: trackAttr
                    ));
                }

                return members;
            });
        }

        /// <summary>
        /// Создает делегат для чтения значения члена
        /// </summary>
        private static Func<object, object?> CreateGetter(MemberInfo member)
        {
            if (member is PropertyInfo pi)
            {
                var getter = pi.GetGetMethod(true)!;
                return obj => getter.Invoke(obj, Array.Empty<object>());
            }
            else if (member is FieldInfo fi)
            {
                return obj => fi.GetValue(obj);
            }

            throw new NotSupportedException($"Unsupported member type: {member.MemberType}");
        }

        /// <summary>
        /// Создает делегат для записи значения члена
        /// </summary>
        private static Action<object, object?>? CreateSetter(MemberInfo member)
        {
            if (member is PropertyInfo pi)
            {
                var setter = pi.GetSetMethod(true);
                if (setter == null) return null;

                return (obj, value) => setter.Invoke(obj, new[] { value });
            }
            else if (member is FieldInfo fi)
            {
                if (fi.IsInitOnly) return null;

                return (obj, value) => fi.SetValue(obj, value);
            }

            throw new NotSupportedException($"Unsupported member type: {member.MemberType}");
        }

        /// <summary>
        /// Получает тип члена
        /// </summary>
        private static Type GetMemberType(MemberInfo member)
        {
            return member switch
            {
                PropertyInfo pi => pi.PropertyType,
                FieldInfo fi => fi.FieldType,
                _ => throw new NotSupportedException($"Unsupported member type: {member.MemberType}")
            };
        }

        /// <summary>
        /// Строит карту [Track]-полей/свойств и подписывает вложенные TrackableNode.
        /// </summary>
        private void BuildTrackMapAndWireChildren()
        {
            var trackedMembers = GetTrackedMembersForType(GetType());

            foreach (var metadata in trackedMembers)
            {
                _trackMap[metadata.Name] = true;

                // Подписываем только TrackableNode-потомков
                if (!typeof(TrackableNode).IsAssignableFrom(metadata.MemberType))
                    continue;

                var current = metadata.Getter(this);
                TrackableNode childNode;

                if (current is TrackableNode existingNode)
                {
                    childNode = existingNode;
                }
                else if (metadata.AutoCreateChildren && !metadata.MemberType.IsAbstract)
                {
                    childNode = (TrackableNode)Activator.CreateInstance(metadata.MemberType)!;
                    metadata.Setter?.Invoke(this, childNode);
                }
                else
                {
                    continue;
                }

                WireChild(metadata.Name, childNode, metadata.WireChildren);
            }
        }

        private void WireChild(string childName, TrackableNode child, bool wireChildren)
        {
            if (_childNodes.TryGetValue(childName, out var existing) && existing != null)
            {
                existing.Changed -= GetChildHandler(childName);
            }

            _childNodes[childName] = child;

            if (wireChildren)
            {
                child.Changed += GetChildHandler(childName);
            }
        }

        private Action<FieldChange> GetChildHandler(string childName)
        {
            return change =>
            {
                // Префиксуем путь именем [Track]-члена (например, "Boxes")
                var newPath = new List<FieldPathSegment> { new FieldPathSegment(childName) };
                newPath.AddRange(change.Path);
                RaiseChange(new FieldChange(newPath, change.OldValue, change.NewValue));
            };
        }

        /// <summary>
        /// Универсальный сеттер для полей/свойств всех наследников.
        /// Поднимает Changed только для [Track]-членов и только для локальных изменений.
        /// </summary>
        protected bool SetProperty<T>(
            ref T field,
            T value,
            bool applyPatch = false,
            [CallerMemberName] string propertyName = ""
        )
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            var oldValue = field;
            field = value;

            // Патч формируем только для локальных изменений и только для [Track]-членов
            if (!applyPatch && _trackMap.TryGetValue(propertyName, out var isTrack) && isTrack)
            {
                var path = new List<FieldPathSegment> { new FieldPathSegment(propertyName) };
                RaiseChange(new FieldChange(path, oldValue, value));
            }

            if (value is TrackableNode childNode)
            {
                // Ищем настройки для этого члена
                var trackedMembers = GetTrackedMembersForType(GetType());
                var metadata = trackedMembers.FirstOrDefault(m => m.Name == propertyName);
                bool wireChildren = metadata?.WireChildren ?? true;

                WireChild(propertyName, childNode, wireChildren);
            }

            return true;
        }

        /// <summary>
        /// Поднимает локальное изменение (без рекурсии по детям).
        /// </summary>
        protected void RaiseLocalChange(
            string fieldName,
            object? oldValue,
            object? newValue
        )
        {
            var path = new List<FieldPathSegment> { new FieldPathSegment(fieldName) };
            RaiseChange(new FieldChange(path, oldValue, newValue));
        }

        /// <summary>
        /// Единая точка выхода изменений наружу.
        /// Наследникам следует использовать именно её, а не вызывать Changed напрямую.
        /// </summary>
        protected virtual void RaiseChange(FieldChange change)
        {
            Changed?.Invoke(change);
        }

        /// <summary>
        /// Очистка подписок (для предотвращения утечек памяти)
        /// </summary>
        public virtual void Dispose()
        {
            var trackedMembers = GetTrackedMembersForType(GetType());

            foreach (var memberName in trackedMembers.Select(m => m.Name))
            {
                if (_childNodes.TryGetValue(memberName, out var child) && child != null)
                {
                    child.Changed -= GetChildHandler(memberName);
                }
            }

            _childNodes.Clear();
            Changed = null;
        }
    }
}