using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Assets.Scripts.Network.NetCore
{
    public static class SimpleSnapshotSerializer
    {
        public static byte[] Serialize(SyncNode node)
        {
            var data = node.GetSerializableData();
            var json = JsonConvert.SerializeObject(data, new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });
            return Encoding.UTF8.GetBytes(json);
        }

        public static void Deserialize(SyncNode node, byte[] data)
        {
            var json = Encoding.UTF8.GetString(data);
            var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

            if (dict != null)
            {
                node.ApplySerializedData(dict);
                node.SnapshotApplied?.Invoke();
            }
        }
    }
    /// <summary>
    /// JSON-сериализатор для сетевых сообщений и состояния.
    /// Подходит для отладки; для продакшена лучше взять MessagePack или свой бинарный формат.
    /// </summary>
    public static class JsonGameSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            TypeNameHandling = TypeNameHandling.None
        };

        public static byte[] Serialize<T>(T obj)
        {
            var json = JsonConvert.SerializeObject(obj, Settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public static T Deserialize<T>(byte[] bytes)
        {
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }
}