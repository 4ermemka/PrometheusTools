using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Базовый класс для всех синхронизируемых объектов.
    /// Отслеживает изменения полей с [Sync] и автоматически подписывается
    /// на вложенные TrackableNode.
    /// </summary>
    public abstract class TrackableNode
    {
        /// <summary>
        /// Событие изменения любого синхронизируемого поля в этом объекте или во вложенных TrackableNode.
        /// </summary>
        public event Action<FieldChange>? Changed;

        private readonly Dictionary<string, bool> _syncMap = new();
        private readonly Dictionary<string, TrackableNode?> _childNodes = new();

        protected TrackableNode()
        {
            BuildSyncMapAndWireChildren();
        }

        /// <summary>
        /// Строит карту sync-полей и подписывает вложенные TrackableNode.
        /// </summary>
        private void BuildSyncMapAndWireChildren()
        {
            var type = GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var member in type.GetMembers(flags))
            {
                var hasSync = member.GetCustomAttribute<SyncAttribute>() != null;
                _syncMap[member.Name] = hasSync;

                Type memberType = null;
                Func<object?> getter = null;
                Action<object?> setter = null;

                if (member is PropertyInfo pi)
                {
                    memberType = pi.PropertyType;
                    if (!pi.CanRead) continue;
                    getter = () => pi.GetValue(this);
                    if (pi.CanWrite)
                        setter = v => pi.SetValue(this, v);
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

                    childNode = (TrackableNode)Activator.CreateInstance(memberType);
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
                var newPath = new List<FieldPathSegment> { new FieldPathSegment(childName) };
                newPath.AddRange(change.Path);
                RaiseChange(new FieldChange(newPath, change.OldValue, change.NewValue));
            };
        }

        /// <summary>
        /// Универсальный сеттер для полей/свойств всех наследников.
        /// </summary>
        protected bool SetProperty<T>(
            ref T field,
            T value,
            [CallerMemberName] string propertyName = ""
        )
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            var oldValue = field;
            field = value;

            if (_syncMap.TryGetValue(propertyName, out var isSync) && isSync)
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
        /// Поднимает событие Changed для текущего узла.
        /// 
        /// Используется как единая точка выхода изменений наружу. Вместо прямого
        /// вызова события Changed в наследниках следует всегда вызывать этот метод.
        /// Это также даёт возможность централизованно добавить логирование, фильтрацию
        /// или буферизацию изменений, не меняя остальной код.
        /// </summary>
        protected virtual void RaiseChange(FieldChange change)
        {
            Changed?.Invoke(change);
        }
    }

}