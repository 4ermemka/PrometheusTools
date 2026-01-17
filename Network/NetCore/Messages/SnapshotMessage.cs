using System;

namespace Assets.Scripts.Network.NetCore
{ 
    public sealed class SnapshotMessage
    {
        // Кому предназначен снапшот
        public Guid TargetClientId { get; set; }

        // Полный JSON авторитетного WorldData
        public byte[] WorldDataPayload { get; set; } = Array.Empty<byte>();
    }
}