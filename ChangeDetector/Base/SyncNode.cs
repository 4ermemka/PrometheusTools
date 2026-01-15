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
        //public event Action<FieldChange>? Patched;
        public event Action? Patched;
        /// <summary>
        /// Применяет патч с генерацией событий Changed (если значение реально изменилось).
        /// Используется для входящих сетевых патчей и снапшотов.
        /// </summary>
        public void ApplyPatchSilently(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            // "Silently" здесь означает "без детектирования по снапшотам",
            // но не подавляет событие Changed: мы сами поднимаем его внутри.
            ApplyPatchInternal(path, 0, newValue);
        }

        /// <summary>
        /// Применяет патч с генерацией событий (то же самое, что ApplyPatchSilently).
        /// Оставлено для семантики API.
        /// </summary>
        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            ApplyPatchInternal(path, 0, newValue);
        }

        /// <summary>
        /// Применение патча по пути внутри дерева SyncNode.
        /// На листе:
        /// - читается старое значение;
        /// - устанавливается новое (с конвертацией);
        /// - при отличии поднимается FieldChange.
        /// </summary>
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
                // лист: меняем значение и создаём FieldChange
                var oldValue = GetMemberValue(member);
                var convertedNewValue = SetMemberValueAndReturn(member, newValue);

                if (!Equals(oldValue, convertedNewValue))
                {
                    var change = new FieldChange(path, oldValue, convertedNewValue);

                    // 1) общее событие Changed (для GameClient → патчи и пр.)
                    RaiseChange(change);

                    //Patched?.Invoke(change);
                    Patched?.Invoke();
                }
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

        /// <summary>
        /// Устанавливает значение поля/свойства с конвертацией и возвращает фактически записанное значение.
        /// </summary>
        private object? SetMemberValueAndReturn(MemberInfo member, object? newValue)
        {
            if (member is PropertyInfo pi)
            {
                var converted = ConvertIfNeeded(newValue, pi.PropertyType);
                pi.SetValue(this, converted);
                return converted;
            }

            if (member is FieldInfo fi)
            {
                var converted = ConvertIfNeeded(newValue, fi.FieldType);
                fi.SetValue(this, converted);
                return converted;
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
