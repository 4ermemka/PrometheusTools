
using System;

namespace Assets.Shared.Network.NetCore
{
    [Serializable]
    public class SnapshotRequestMessage
    {
        /// <summary>
        /// Идентификатор клиента, запрашивающего снапшот
        /// </summary>
        public Guid RequestorClientId { get; set; }

        /// <summary>
        /// Причина запроса (connect/reconnect/state-corruption)
        /// </summary>
        public string Reason { get; set; } = "connect";
    }
}