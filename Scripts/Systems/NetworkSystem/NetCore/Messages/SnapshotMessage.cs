using System;
using System.Collections.Generic;

namespace Assets.Scripts.Network.NetCore
{
    [Serializable]
    public class SnapshotMessage
    {
        /// <summary>
        /// Идентификатор клиента, которому предназначен снапшот
        /// </summary>
        public Guid TargetClientId { get; set; }

        /// <summary>
        /// Сериализованное состояние мира в формате JSON
        /// Используется WorldState для сериализации/десериализации
        /// </summary>
        public string WorldDataPayload { get; set; }

        /// <summary>
        /// Версия снапшота (может быть хэш или timestamp)
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// Список примененных патчей (для дебага или восстановления)
        /// </summary>
        public List<ChangeData> AppliedPatches { get; set; } = new();
    }
}