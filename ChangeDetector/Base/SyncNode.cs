using System;
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
            SyncNode root,   // всегда корневой WorldData, именно на нём вызываем ApplyPatch
            SyncNode target, // текущий узел, по которому бежим рекурсией
            SyncNode source,
            List<FieldPathSegment> path)
        {
            var type = target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var member in type.GetMembers(flags))
            {
                if (member is not FieldInfo && member is not PropertyInfo)
                    continue;

                // пропускаем backing-fields
                if (member.Name.StartsWith("_"))
                    continue;

                var memberType = GetMemberType(member);

                var valueSource = GetMemberValue(member, source);
                var valueTarget = GetMemberValue(member, target);

                if (valueSource == null && typeof(SyncNode).IsAssignableFrom(memberType))
                {
                    Debug.LogWarning($"[SyncNode] GetMemberValue returned null for {type.Name}.{member.Name}");
                }

                // добавляем сегмент имени члена
                path.Add(new FieldPathSegment(member.Name));

                if (typeof(SyncNode).IsAssignableFrom(memberType) &&
                    valueSource is SyncNode childSource &&
                    valueTarget is SyncNode childTarget)
                {
                    // Вложенный SyncNode: рекурсивно внутрь,
                    // root остаётся тем же (WorldData), target/source идут вниз.
                    ApplySnapshotRecursive(root, childTarget, childSource, path);
                }
                else
                {
                    // Лист: вызываем ApplyPatch на КОРНЕ с ПОЛНЫМ путём.
                    var fullPath = new List<FieldPathSegment>(path);
                    root.ApplyPatch(fullPath, valueSource);

                }

                // убираем последний сегмент перед переходом к следующему члену
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

            // 1. Сначала пробуем backing-field: _boxData, _position и т.п.
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
                member = backingField; // гарантированно FieldInfo → SetMemberValue не тронет SetProperty
            }
            else
            {
                // 2. Если backing-field нет, работаем как раньше (для несинхронных членов)
                member = (MemberInfo?)type.GetField(segmentName, flags)
                         ?? type.GetProperty(segmentName, flags)
                         ?? throw new InvalidOperationException(
                             $"Member '{segmentName}' not found on '{type.Name}'.");
            }

            bool isLast = index == path.Count - 1;

            if (isLast)
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
