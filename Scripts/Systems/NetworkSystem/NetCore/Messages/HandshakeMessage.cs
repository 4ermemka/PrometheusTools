using System;

namespace Assets.Scripts.Network.NetCore
{
    [Serializable]
    public class HandshakeMessage
    {
        public int ProtocolVersion { get; set; }
        public bool IsHost { get; set; }
        public Guid ClientId { get; set; }
        public Guid ConnectionId { get; set; }
        public Guid ServerSessionId { get; set; }
        public bool IsReconnect { get; set; }
        public long ServerSequence { get; set; }
        public long ServerTime { get; set; }
    }
}
