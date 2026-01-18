using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Assets.Shared.ChangeDetector.Base.Mapping
{
    /// <summary>
    /// Кэшированная информация о члене
    /// </summary>
    public class MemberMetadata
    {
        public string Name { get; }
        public MemberInfo Member { get; }
        public Type MemberType { get; }
        public Func<object, object?> Getter { get; }
        public Action<object, object?>? Setter { get; }
        public bool IsTracked { get; }
        public bool IsPatchable { get; }
        public string? BackingFieldName { get; }

        public MemberMetadata(
            string name,
            MemberInfo member,
            Type memberType,
            Func<object, object?> getter,
            Action<object, object?>? setter,
            bool isTracked,
            bool isPatchable,
            string? backingFieldName)
        {
            Name = name;
            Member = member;
            MemberType = memberType;
            Getter = getter;
            Setter = setter;
            IsTracked = isTracked;
            IsPatchable = isPatchable;
            BackingFieldName = backingFieldName;
        }
    }

    /// <summary>
    /// Кэш метаданных по типам
    /// </summary>
    public static class MemberMetadataCache
    {
        private static readonly ConcurrentDictionary<Type, MemberMetadata[]> _cache = new();
        private static readonly ConcurrentDictionary<Type, Dictionary<string, MemberMetadata>> _byNameCache = new();

        public static IReadOnlyList<MemberMetadata> GetMembers(Type type)
        {
            return _cache.GetOrAdd(type, BuildMembers);
        }

        public static bool TryGetMember(Type type, string name, out MemberMetadata? metadata)
        {
            var dict = _byNameCache.GetOrAdd(type, t =>
                BuildMembers(t).ToDictionary(m => m.Name));

            return dict.TryGetValue(name, out metadata);
        }

        private static MemberMetadata[] BuildMembers(Type type)
        {
            var members = new List<MemberMetadata>();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var member in type.GetMembers(flags))
            {
                if (member is not FieldInfo && member is not PropertyInfo)
                    continue;

                // Проверяем атрибуты
                var hasTrack = member.GetCustomAttribute<TrackAttribute>(inherit: true) != null;
                var hasPatchable = member.GetCustomAttribute<PatchableAttribute>(inherit: true) != null;

                // Пропускаем члены без атрибутов
                if (!hasTrack && !hasPatchable)
                    continue;

                // Для свойств проверяем доступность
                if (member is PropertyInfo pi)
                {
                    if (pi.GetIndexParameters().Length > 0)
                        continue; // Индексаторы

                    if (pi.GetGetMethod(true) == null)
                        continue; // Нет геттера

                    // Для Track-атрибута проверяем сеттер
                    if (hasTrack && pi.GetSetMethod(true) == null)
                        continue;
                }

                // Получаем имя backing-поля
                string? backingField = null;
                if (hasPatchable)
                {
                    var patchableAttr = member.GetCustomAttribute<PatchableAttribute>(inherit: true);
                    if (!string.IsNullOrEmpty(patchableAttr?.BackingField))
                    {
                        backingField = patchableAttr.BackingField;
                    }
                }

                // Создаем делегаты для доступа
                var getter = CreateGetter(member);
                var setter = CreateSetter(member);
                var memberType = GetMemberType(member);

                members.Add(new MemberMetadata(
                    name: member.Name,
                    member: member,
                    memberType: memberType,
                    getter: getter,
                    setter: setter,
                    isTracked: hasTrack,
                    isPatchable: hasPatchable,
                    backingFieldName: backingField
                ));
            }

            return members.ToArray();
        }

        private static Func<object, object?> CreateGetter(MemberInfo member)
        {
            if (member is PropertyInfo pi)
            {
                var getter = pi.GetGetMethod(true)!;
                return obj => getter.Invoke(obj, Array.Empty<object>());
            }
            else if (member is FieldInfo fi)
            {
                return obj => fi.GetValue(obj);
            }

            throw new NotSupportedException($"Unsupported member type: {member.MemberType}");
        }

        private static Action<object, object?>? CreateSetter(MemberInfo member)
        {
            if (member is PropertyInfo pi)
            {
                var setter = pi.GetSetMethod(true);
                if (setter == null) return null;

                return (obj, value) => setter.Invoke(obj, new[] { value });
            }
            else if (member is FieldInfo fi)
            {
                if (fi.IsInitOnly) return null;

                return (obj, value) => fi.SetValue(obj, value);
            }

            throw new NotSupportedException($"Unsupported member type: {member.MemberType}");
        }

        private static Type GetMemberType(MemberInfo member) =>
            member switch
            {
                PropertyInfo pi => pi.PropertyType,
                FieldInfo fi => fi.FieldType,
                _ => throw new NotSupportedException()
            };
    }
}