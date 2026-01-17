using System;
using System.Collections.Generic;
using System.Reflection;

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
            Patched?.Invoke();
        }

        /// <summary>
        /// Применяет полный снапшот: копирует все данные из другого SyncNode
        /// в текущий экземпляр, рекурсивно по всем полям/свойствам.
        /// Не поднимает Changed и не генерирует патчи.
        /// </summary>
        /// <param name="source">Источник данных (обычно десериализованный WorldData из снапшота).</param>
        public void ApplySnapshot(SyncNode source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (source.GetType() != GetType())
                throw new InvalidOperationException(
                    $"ApplySnapshot type mismatch: source={source.GetType().Name}, target={GetType().Name}");

            var path = new List<FieldPathSegment>();
            ApplySnapshotRecursive(this, source, path);

            Patched?.Invoke();
        }

        /// <summary>
        /// Рекурсивное применение снапшота: копирует значения из source в target.
        /// </summary>
        private static void ApplySnapshotRecursive(SyncNode target, SyncNode source, List<FieldPathSegment> path)
        {
            var type = target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Берём только поля/свойства, которые реально хотим синхронизировать.
            // Здесь можно добавить фильтрацию по атрибуту [Sync], если он есть.
            foreach (var member in type.GetMembers(flags))
            {
                if (member is not FieldInfo && member is not PropertyInfo)
                    continue;

                // Пропускаем backing-fields (_boxData и т.п.), работаем по "публичному" имени.
                if (member.Name.StartsWith("_"))
                    continue;

                var memberType = GetMemberType(member);

                var valueSource = GetMemberValue(member, source);
                var valueTarget = GetMemberValue(member, target);

                path.Add(new FieldPathSegment(member.Name));

                if (typeof(SyncNode).IsAssignableFrom(memberType) &&
                    valueSource is SyncNode childSource &&
                    valueTarget is SyncNode childTarget)
                {
                    // Вложенный SyncNode: рекурсивно внутрь.
                    ApplySnapshotRecursive(childTarget, childSource, path);
                }
                else
                {
                    // Лист: применяем через ApplyPatch, который пишет в backing-field.
                    target.ApplyPatch(path, valueSource);
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

            // Пытаемся найти backing-field по конвенции: BoxData -> _boxData
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
                member = (MemberInfo?)type.GetProperty(segmentName, flags)
                         ?? type.GetField(segmentName, flags)
                         ?? throw new InvalidOperationException(
                             $"Member '{segmentName}' not found on '{type.Name}'.");
            }

            bool isLast = index == path.Count - 1;

            if (isLast)
            {
                // Лист: пишем значение в backing-field / поле / свойство.
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

        private static Type GetMemberType(MemberInfo member) =>
            member switch
            {
                PropertyInfo pi => pi.PropertyType,
                FieldInfo fi => fi.FieldType,
                _ => throw new NotSupportedException($"Unsupported member type: {member.MemberType}")
            };

        private static object? GetMemberValue(MemberInfo member, object instance) =>
            member switch
            {
                PropertyInfo pi => pi.GetValue(instance),
                FieldInfo fi => fi.GetValue(instance),
                _ => throw new NotSupportedException($"Unsupported member type: {member.MemberType}")
            };

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
