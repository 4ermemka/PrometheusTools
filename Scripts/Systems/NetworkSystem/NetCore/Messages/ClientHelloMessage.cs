using System;

namespace Assets.Scripts.Network.NetCore
{
    public static class NetworkProtocol { public const int Version = 3; }

    [Serializable]
    public sealed class ClientHelloMessage
    {
        public int ProtocolVersion { get; set; }
        public Guid ClientId { get; set; }
        public string ReconnectToken { get; set; }
    }

    [Serializable]
    public sealed class ConnectionRejectedMessage
    {
        public string Reason { get; set; }
    }

    [Serializable]
    public sealed class SnapshotProviderMessage
    {
        public bool IsSnapshotProvider { get; set; }
    }
}
