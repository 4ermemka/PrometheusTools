using Assets.Shared.ChangeDetector.Base;
using System;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector.Base.Mapping
{
    public static class SyncValueMapperRegistry
    {
        private static readonly Dictionary<Type, ISyncValueMapper> _modelToMapper =
        new Dictionary<Type, ISyncValueMapper>();

        private static readonly Dictionary<Type, ISyncValueMapper> _dtoToMapper =
            new Dictionary<Type, ISyncValueMapper>();

        private static bool _initialized;

        public static IEnumerable<Type> GetAllDtoTypes()
        {
            EnsureInitialized();
            return _dtoToMapper.Keys;
        }

        public static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (!typeof(ISyncValueMapper).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                        continue;

                    var attrs = (SyncDtoAttribute[])type.GetCustomAttributes(typeof(SyncDtoAttribute), inherit: false);
                    if (attrs == null || attrs.Length == 0)
                        continue;

                    var mapper = (ISyncValueMapper)Activator.CreateInstance(type);
                    foreach (var attr in attrs)
                    {
                        _modelToMapper[attr.ModelType] = mapper;
                        _dtoToMapper[type] = mapper;
                    }
                }
            }
        }

        public static bool TryGetMapperForModel(Type modelType, out ISyncValueMapper mapper)
        {
            EnsureInitialized();
            return _modelToMapper.TryGetValue(modelType, out mapper);
        }

        public static bool TryGetMapperForDto(Type dtoType, out ISyncValueMapper mapper)
        {
            EnsureInitialized();
            return _dtoToMapper.TryGetValue(dtoType, out mapper);
        }
    }
}
