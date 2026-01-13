using System;
using System.Collections.Generic;
using System.Reflection;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Расширение TrackableNode, которое умеет применять входящие патчи по пути.
    /// Используется на принимающей стороне (клиенты и хост, принимающий патчи от других).
    /// </summary>
    public abstract class SyncNode : TrackableNode
    {
        private bool _suppressChanges;

        /// <summary>
        /// Применяет патч так, чтобы НЕ генерировать собственные события Changed.
        /// Используется для входящих сетевых патчей и снапшотов.
        /// </summary>
        public void ApplyPatchSilently(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            _suppressChanges = true;
            try
            {
                ApplyPatchInternal(path, 0, newValue);
            }
            finally
            {
                _suppressChanges = false;
            }
        }

        /// <summary>
        /// Применяет патч с генерацией событий (если нужно).
        /// Реже используется, обычно достаточно Silently.
        /// </summary>
        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            ApplyPatchInternal(path, 0, newValue);
        }

        protected override void RaiseChange(FieldChange change)
        {
            if (_suppressChanges)
                return;
            base.RaiseChange(change);
        }

        private void ApplyPatchInternal(IReadOnlyList<FieldPathSegment> path, int index, object? newValue)
        {
            var segmentName = path[index].Name;
            var type = GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var member = (MemberInfo)type.GetProperty(segmentName, flags)
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
            }
            else if (member is FieldInfo fi)
            {
                var converted = ConvertIfNeeded(newValue, fi.FieldType);
                fi.SetValue(this, converted);
            }
            else
            {
                throw new NotSupportedException($"Unsupported member type: {member.MemberType}");
            }
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