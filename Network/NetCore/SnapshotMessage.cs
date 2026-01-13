using System;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// Сетевое сообщение со снапшотом всего состояния WorldState.
    /// </summary>
    public sealed class SnapshotMessage
    {
        /// <summary>
        /// Сериализованное состояние WorldState (например, JSON или MessagePack).
        /// </summary>
        public byte[] WorldStatePayload { get; set; }

        public SnapshotMessage()
        {
            WorldStatePayload = Array.Empty<byte>();
        }
    }
}