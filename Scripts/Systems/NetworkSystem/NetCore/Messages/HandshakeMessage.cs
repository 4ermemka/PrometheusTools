using System;

namespace Assets.Scripts.Network.NetCore
{
    [Serializable]
    public class HandshakeMessage
    {
        public bool IsHost { get; set; }
        public Guid ClientId { get; set; }
        public long ServerTime { get; set; }
    }
}
