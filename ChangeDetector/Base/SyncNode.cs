using System;
using System.Collections.Generic;
using System.Reflection;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Узел, который умеет применять входящие патчи по пути.
    /// Не поднимает Changed, только своё событие Patched.
    /// </summary>
    public abstract class SyncNode : TrackableNode
    {
        /// <summary>
        /// Срабатывает, когда к узлу был применён патч (или снапшот).
        /// Не предназначено для отправки исходящих патчей.
        /// </summary>
        public event Action? Patched;

        /// <summary>
        /// Применяет патч по пути без генерации Changed.
        /// </summary>
        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            ApplyPatchInternal(path, 0, newValue);
            Patched?.Invoke();
        }

        /// <summary>
        /// Технический рекурсивный проход по пути.
        /// </summary>
        private void ApplyPatchInternal(IReadOnlyList<FieldPathSegment> path, int index, object? newValue)
        {
            var segmentName = path[index].Name;
            var type = GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var member = (MemberInfo?)type.GetProperty(segmentName, flags)
                         ?? type.GetField(segmentName, flags);

            if (member == null)
                throw new InvalidOperationException($"Member '{segmentName}' not found on '{type.Name}'.");

            bool isLast = index == path.Count - 1;

            if (isLast)
            {
                SetMemberValue(member, newValue);
            }
            else
            {
                var childValue = GetMemberValue(member);

                if (childValue is SyncNode childSync)
                {
                    childSync.ApplyPatchInternal(path, index + 1, newValue);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Member '{segmentName}' on '{type.Name}' is not SyncNode; cannot continue path.");
                }
            }
        }

        private object? GetMemberValue(MemberInfo member)
        {
            if (member is PropertyInfo pi)
                return pi.GetValue(this);
            if (member is FieldInfo fi)
                return fi.GetValue(this);

            throw new NotSupportedException($"Unsupported member type: {member.MemberType}");
        }

        private void SetMemberValue(MemberInfo member, object? newValue)
        {
            if (member is PropertyInfo pi)
            {
                var converted = ConvertIfNeeded(newValue, pi.PropertyType);
                pi.SetValue(this, converted);
                return;
            }

            if (member is FieldInfo fi)
            {
                var converted = ConvertIfNeeded(newValue, fi.FieldType);
                fi.SetValue(this, converted);
                return;
            }

            throw new NotSupportedException($"Unsupported member type: {member.MemberType}");
        }

        private object? ConvertIfNeeded(object? value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType.IsInstanceOfType(value))
                return value;

            return Convert.ChangeType(value, targetType);
        }
    }
}
