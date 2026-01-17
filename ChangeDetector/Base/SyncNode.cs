using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Узел, который умеет применять входящие патчи и снапшоты.
    /// При этом не поднимает Changed/FieldChange и не формирует исходящие патчи.
    /// </summary>
    public abstract class SyncNode : TrackableNode
    {
        /// <summary>
        /// Срабатывает, когда к узлу был применён патч или снапшот.
        /// Не предназначено для отправки исходящих патчей.
        /// </summary>
        public event Action? Patched;

        /// <summary>
        /// Применяет патч по пути без генерации Changed.
        /// </summary>
        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            ApplyPatchInternal(path, 0, newValue);
        }

        /// <summary>
        /// Рекурсивное применение снапшота: копирует значения из source в target.
        /// </summary>
        public void ApplySnapshot(SyncNode source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (source.GetType() != GetType())
                throw new InvalidOperationException(
                    $"ApplySnapshot type mismatch: source={source.GetType().Name}, target={GetType().Name}");

            var path = new List<FieldPathSegment>();

            // ВАЖНО: root = this (WorldData), и именно на нём всегда вызывается ApplyPatch.
            ApplySnapshotRecursive(root: this, target: this, source: source, path);
        }

        private static void ApplySnapshotRecursive(
            SyncNode root,
            SyncNode target,
            SyncNode source,
            List<FieldPathSegment> path)
        {
            var type = target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var member in type.GetMembers(flags))
            {
                if (member is not FieldInfo && member is not PropertyInfo)
                    continue;

                // Пропускаем backing-fields (_xxx)
                if (member.Name.StartsWith("_", StringComparison.Ordinal))
                    continue;

                if (member is PropertyInfo pi)
                {
                    var getter = pi.GetGetMethod(true);
                    if (getter == null)
                        continue;

                    // Пропускаем индексаторы и свойства с параметрами
                    if (getter.GetParameters().Length > 0)
                        continue;

                    // Пропускаем свойства только с getter (Count, IsReadOnly и т.п.)
                    var setter = pi.GetSetMethod(true);
                    if (setter == null)
                        continue;
                }

                var memberType = GetMemberType(member);

                var valueSource = GetMemberValue(member, source);
                var valueTarget = GetMemberValue(member, target);

                path.Add(new FieldPathSegment(member.Name));

                if (typeof(SyncNode).IsAssignableFrom(memberType) &&
                    valueSource is SyncNode childSource &&
                    valueTarget is SyncNode childTarget)
                {
                    ApplySnapshotRecursive(root, childTarget, childSource, path);
                }
                else
                {
                    var fullPath = new List<FieldPathSegment>(path);
                    root.ApplyPatch(fullPath, valueSource);
                }

                path.RemoveAt(path.Count - 1);
            }
        }



        /// <summary>
        /// Рекурсивное применение патча по пути.
        /// На листе пишет значение обычно в backing-field (если найден).
        /// </summary>
        private void ApplyPatchInternal(IReadOnlyList<FieldPathSegment> path, int index, object? newValue)
        {
            var segmentName = path[index].Name;
            var type = GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 1. Если текущий объект – коллекция и сегмент – индекс, обрабатываем как элемент списка
            if (this is IList list && TryParseIndex(segmentName, out var elementIndex))
            {
                bool isLast = index == path.Count - 1;

                if (isLast)
                {
                    // Лист: заменяем элемент целиком (для патчей Replace по элементу)
                    var converted = ConvertIfNeeded(newValue, typeof(object));
                    list[elementIndex] = converted;
                    Patched?.Invoke();
                    return;
                }
                else
                {
                    // Спускаемся в элемент списка, если он SyncNode
                    var item = list[elementIndex];

                    if (item is SyncNode childSync)
                    {
                        childSync.ApplyPatchInternal(path, index + 1, newValue);
                        return;
                    }

                    throw new InvalidOperationException(
                        $"List element at index {elementIndex} on '{type.Name}' is not SyncNode; cannot continue path.");
                }
            }

            // 2. Обычный случай: работаем с полями/свойствами
            FieldInfo? backingField = null;
            if (!string.IsNullOrEmpty(segmentName))
            {
                var backingFieldName =
                    "_" + char.ToLowerInvariant(segmentName[0]) + segmentName.Substring(1);

                backingField = type.GetField(backingFieldName, flags);
            }

            MemberInfo member;

            if (backingField != null)
            {
                member = backingField;
            }
            else
            {
                member = (MemberInfo?)type.GetField(segmentName, flags)
                         ?? type.GetProperty(segmentName, flags)
                         ?? throw new InvalidOperationException(
                             $"Member '{segmentName}' not found on '{type.Name}'.");
            }

            bool isLeaf = index == path.Count - 1;

            if (isLeaf)
            {
                SetMemberValue(member, newValue);
                Patched?.Invoke();
            }
            else
            {
                var childValue = GetMemberValue(member, this);

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

        private void SetMemberValue(MemberInfo member, object? newValue)
        {
            if (member is FieldInfo fi)
            {
                // Пишем ТОЛЬКО в поле (в том числе backing-field), минуя сеттеры.
                var converted = ConvertIfNeeded(newValue, fi.FieldType);
                fi.SetValue(this, converted);
                return;
            }

            if (member is PropertyInfo pi)
            {
                // Сюда должны попадать только "несетевые" свойства,
                // которые НЕ используют SetProperty и не помечены [Sync].
                var converted = ConvertIfNeeded(newValue, pi.PropertyType);
                pi.SetValue(this, converted);
                return;
            }

            throw new NotSupportedException($"Unsupported member type: {member.MemberType}");
        }

        private static Type GetMemberType(MemberInfo member) =>
            member switch
            {
                PropertyInfo pi => pi.PropertyType,
                FieldInfo fi => fi.FieldType,
                _ => throw new NotSupportedException($"Unsupported member type: {member.MemberType}")
            };

        private static object? GetMemberValue(MemberInfo member, object instance)
        {
            switch (member)
            {
                case PropertyInfo pi:
                    {
                        // Защита от "нестандартных" геттеров, которые ломают CreateDelegate внутри GetValue
                        var getter = pi.GetGetMethod(true);
                        if (getter == null)
                            return null;

                        // Индексаторы и прочие с параметрами — не трогаем
                        if (getter.GetParameters().Length > 0)
                            return null;

                        // Если тип declaring/target не совпадает, тоже лучше пропустить
                        if (getter.IsStatic)
                            return getter.Invoke(null, Array.Empty<object>());

                        return getter.Invoke(instance, Array.Empty<object>());
                    }

                case FieldInfo fi:
                    return fi.GetValue(instance);

                default:
                    throw new NotSupportedException($"Unsupported member type: {member.MemberType}");
            }
        }

        private static bool TryParseIndex(string segmentName, out int index)
        {
            // ожидаем формат "[3]" или "3"
            if (segmentName.Length > 2 && segmentName[0] == '[' && segmentName[^1] == ']')
                segmentName = segmentName.Substring(1, segmentName.Length - 2);

            return int.TryParse(segmentName, out index);
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
