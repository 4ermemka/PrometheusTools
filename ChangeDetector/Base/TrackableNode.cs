using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Базовый класс для всех синхронизируемых объектов.
    /// Отслеживает изменения полей/свойств с [Sync] и автоматически подписывается
    /// на вложенные TrackableNode.
    /// </summary>
    public abstract class TrackableNode
    {
        /// <summary>
        /// Событие изменения любого синхронизируемого поля в этом объекте или во вложенных TrackableNode.
        /// </summary>
        public event Action<FieldChange>? Changed;

        // Карта: имя члена -> является ли он [Sync]
        private readonly Dictionary<string, bool> _syncMap = new();

        // Карта: имя дочернего члена -> TrackableNode (для bubbling)
        private readonly Dictionary<string, TrackableNode?> _childNodes = new();

        protected TrackableNode()
        {
            BuildSyncMapAndWireChildren();
        }

        /// <summary>
        /// Строит карту [Sync]-полей/свойств и подписывает вложенные TrackableNode.
        /// </summary>
        private void BuildSyncMapAndWireChildren()
        {
            var type = GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var member in type.GetMembers(flags))
            {
                if (member is not FieldInfo && member is not PropertyInfo)
                    continue;

                // Берём только члены, помеченные [Sync]
                var hasSync = member.GetCustomAttribute<SyncAttribute>(inherit: true) != null;
                _syncMap[member.Name] = hasSync;

                if (!hasSync)
                    continue;

                Type? memberType = null;
                Func<object?>? getter = null;
                Action<object?>? setter = null;

                if (member is PropertyInfo pi)
                {
                    memberType = pi.PropertyType;
                    if (!pi.CanRead)
                        continue;

                    // Пропускаем индексаторы и свойства с параметрами
                    var indexParams = pi.GetIndexParameters();
                    if (indexParams is { Length: > 0 })
                        continue;

                    var getterMethod = pi.GetGetMethod(true);
                    if (getterMethod == null)
                        continue;

                    getter = () => getterMethod.Invoke(this, Array.Empty<object>());

                    if (pi.CanWrite)
                    {
                        var setterMethod = pi.GetSetMethod(true);
                        if (setterMethod != null)
                        {
                            var setterParams = setterMethod.GetParameters();
                            if (setterParams.Length == 1)
                            {
                                setter = v => setterMethod.Invoke(this, new[] { v });
                            }
                        }
                    }
                }
                else if (member is FieldInfo fi)
                {
                    memberType = fi.FieldType;
                    getter = () => fi.GetValue(this);
                    if (!fi.IsInitOnly)
                        setter = v => fi.SetValue(this, v);
                }

                if (memberType == null)
                    continue;

                // Подписываем только TrackableNode-потомков
                if (!typeof(TrackableNode).IsAssignableFrom(memberType))
                    continue;

                var current = getter();
                TrackableNode childNode;

                if (current is TrackableNode existingNode)
                {
                    childNode = existingNode;
                }
                else
                {
                    if (memberType.IsAbstract)
                        continue;

                    childNode = (TrackableNode)Activator.CreateInstance(memberType)!;
                    setter?.Invoke(childNode);
                }

                WireChild(member.Name, childNode);
            }
        }

        private void WireChild(string childName, TrackableNode child)
        {
            if (_childNodes.TryGetValue(childName, out var existing) && existing != null)
            {
                existing.Changed -= GetChildHandler(childName);
            }

            _childNodes[childName] = child;
            child.Changed += GetChildHandler(childName);
        }

        private Action<FieldChange> GetChildHandler(string childName)
        {
            return change =>
            {
                // Префиксуем путь именем [Sync]-члена (например, "Boxes")
                var newPath = new List<FieldPathSegment> { new FieldPathSegment(childName) };
                newPath.AddRange(change.Path);
                RaiseChange(new FieldChange(newPath, change.OldValue, change.NewValue));
            };
        }

        /// <summary>
        /// Универсальный сеттер для полей/свойств всех наследников.
        /// Поднимает Changed только для [Sync]-членов и только для локальных изменений.
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

            // Патч формируем только для локальных изменений и только для [Sync]-членов
            if (!applyPatch &&
                _syncMap.TryGetValue(propertyName, out var isSync) && isSync)
            {
                var path = new List<FieldPathSegment> { new FieldPathSegment(propertyName) };
                RaiseChange(new FieldChange(path, oldValue, value));
            }

            if (value is TrackableNode childNode)
            {
                WireChild(propertyName, childNode);
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
    }
}
