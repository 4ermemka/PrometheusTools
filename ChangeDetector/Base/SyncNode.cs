using Assets.Shared.ChangeDetector.Collections;
using Newtonsoft.Json;
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
        /// Срабатывает при применении входящего патча (точечное изменение).
        /// </summary>
        [JsonIgnore]
        public Action? Patched;

        /// <summary>
        /// Срабатывает один раз после применения полного снапшота к этому узлу.
        /// Используется для переподписки на заменённые объекты.
        /// </summary>
        [JsonIgnore]
        public Action? SnapshotApplied;

        /// <summary>
        /// Применяет патч по пути без генерации Changed.
        /// </summary>
        public void ApplyPatch(IReadOnlyList<FieldPathSegment> path, object? newValue)
        {
            if (path == null || path.Count == 0)
                throw new ArgumentException("Path must be non-empty.", nameof(path));

            ApplyPatchInternal(path, 0, newValue);
        }

        /// <summary>
        /// Применяет полный снапшот: копирует все данные из другого SyncNode
        /// в текущий экземпляр, рекурсивно по всем полям/свойствам.
        /// Не поднимает Changed и не генерирует патчи, только Patched на изменённых узлах.
        /// </summary>
        public void ApplySnapshot(SyncNode source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (source.GetType() != GetType())
                throw new InvalidOperationException(
                    $"ApplySnapshot type mismatch: source={source.GetType().Name}, target={GetType().Name}");

            var path = new List<FieldPathSegment>();

            // root = this (обычно WorldData), именно на нём всегда вызываем ApplyPatch.
            ApplySnapshotRecursive(root: this, target: this, source: source, path);
            // после полного прохода по дереву
            SnapshotApplied?.Invoke();
        }

        /// <summary>
        /// Рекурсивное применение снапшота: обходим дерево source/target
        /// и для каждого "листа" вызываем root.ApplyPatch с полным путём.
        /// Для коллекций делегируем применение снапшота самим коллекциям.
        /// </summary>
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

                var hasSyncAttr = member.GetCustomAttribute<SyncAttribute>(inherit: true) != null;
                if (!hasSyncAttr)
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

                if (typeof(ISnapshotCollection).IsAssignableFrom(memberType) &&
                    valueTarget is ISnapshotCollection targetCollection)
                {
                    targetCollection.ApplySnapshotFrom(valueSource);
                }
                else if (typeof(SyncNode).IsAssignableFrom(memberType) &&
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

            // Если текущий объект сам коллекция и дальше есть элементы пути — передаём в неё.
            if (this is ISyncIndexableCollection selfCollection && index < path.Count)
            {
                ApplyPatchIntoCollection(selfCollection, path, index, newValue);
                return;
            }

            // 1. Обычный путь по полям/свойствам
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

            bool isLast = index == path.Count - 1;

            if (isLast)
            {
                SetMemberValue(member, newValue);
                Patched?.Invoke();
            }
            else
            {
                var childValue = GetMemberValue(member, this);

                if (childValue is ISyncIndexableCollection collection)
                {
                    // Член – коллекция: следующий сегмент трактуем как "ключ/индекс" коллекции
                    ApplyPatchIntoCollection(collection, path, index + 1, newValue);
                }
                else if (childValue is SyncNode childSync)
                {
                    childSync.ApplyPatchInternal(path, index + 1, newValue);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Member '{segmentName}' on '{type.Name}' is not SyncNode or ISyncIndexableCollection; cannot continue path.");
                }
            }
        }

        /// <summary>
        /// Применение патча внутрь коллекции: текущий сегмент (или следующий) трактуется как "индекс/ключ".
        /// </summary>
        private void ApplyPatchIntoCollection(
            ISyncIndexableCollection collection,
            IReadOnlyList<FieldPathSegment> path,
            int index,
            object? newValue)
        {
            if (index >= path.Count)
                throw new InvalidOperationException("Path ended before collection element.");

            var segmentName = path[index].Name;
            bool isLeaf = index == path.Count - 1;

            if (isLeaf)
            {
                // Изменение самого элемента коллекции (Replace)
                collection.SetElement(segmentName, newValue);
                if(collection is SyncNode syncCollection)
                {
                    syncCollection?.Patched?.Invoke();
                }
            }
            else
            {
                // Изменение внутри элемента: Boxes[3].Position / Dictionary["key"].Field
                var item = collection.GetElement(segmentName);

                if (item is SyncNode childSync)
                {
                    childSync.ApplyPatchInternal(path, index + 1, newValue);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Element '{segmentName}' of '{collection.GetType().Name}' is not SyncNode.");
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
                var setter = pi.GetSetMethod(true);
                if (setter == null)
                    return;

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
                        var getter = pi.GetGetMethod(true);
                        if (getter == null)
                            return null;

                        // Индексаторы и свойства с параметрами уже отфильтрованы, но на всякий случай
                        if (getter.GetParameters().Length > 0)
                            return null;

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

        protected object? ConvertIfNeeded(object? value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType.IsInstanceOfType(value))
                return value;

            return Convert.ChangeType(value, targetType);
        }
    }
}
