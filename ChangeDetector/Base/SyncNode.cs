using System;
using System.Collections.Generic;
using System.Reflection;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Расширение TrackableNode, которое может не только отслеживать изменения,
    /// но и применять входящий патч по пути (для клиента, получающего данные по сети).
    /// </summary>
    public abstract class SyncNode : TrackableNode
    {
        /// <summary>
        /// Применяет изменение к текущему узлу по заданному пути.
        /// Используется на принимающей стороне для обновления состояния по патчу.
        /// </summary>
        /// <param name="path">Путь сегментов от этого узла вниз.</param>
        /// <param name="newValue">Новое значение для конечного поля.</param>
        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            if (path == null || path.Count == 0)
                throw new ArgumentException("Path must not be empty.", nameof(path));

            ApplyPatchInternal(path, 0, newValue);
        }

        /// <summary>
        /// Рекурсивное применение патча, начиная с указанного индекса сегмента.
        /// </summary>
        private void ApplyPatchInternal(IReadOnlyList<FieldPathSegment> path, int index, object? newValue)
        {
            var segment = path[index].Name;
            var type = GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Ищем свойство или поле с таким именем
            var member = (MemberInfo)type.GetProperty(segment, flags)
                         ?? type.GetField(segment, flags);

            if (member == null)
                throw new InvalidOperationException($"Member '{segment}' not found on type '{type.Name}'.");

            bool isLast = index == path.Count - 1;

            if (isLast)
            {
                // Базовый случай: присваиваем значение полю/свойству
                SetMemberValue(member, newValue);
            }
            else
            {
                var childValue = GetMemberValue(member);

                if (childValue is SyncNode childSync)
                {
                    childSync.ApplyPatchInternal(path, index + 1, newValue);
                }
                else if (childValue is TrackableNode childTrackable)
                {
                    throw new InvalidOperationException(
                        $"Member '{segment}' on '{type.Name}' is TrackableNode but not SyncNode; cannot apply patch.");
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Member '{segment}' on '{type.Name}' is not a TrackableNode; cannot continue path.");
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

        /// <summary>
        /// Простейшая конвертация входного значения к целевому типу.
        /// На практике здесь может быть десериализация из строки/JSON и т.п.
        /// </summary>
        private object? ConvertIfNeeded(object? value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType.IsInstanceOfType(value))
                return value;

            // Попытка через System.Convert (для примитивов)
            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                // В реальном коде: логировать и/или бросать специализированное исключение
                throw;
            }
        }
    }

}