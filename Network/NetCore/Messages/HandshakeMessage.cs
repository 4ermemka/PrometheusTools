using System;

namespace Assets.Shared.Network.NetCore.Messages
{
    // Handshake сообщение
    [Serializable]
    public class HandshakeMessage
    {
        public bool IsHost { get; set; }
        public Guid ClientId { get; set; }
        public long ServerTime { get; set; }
    }
}