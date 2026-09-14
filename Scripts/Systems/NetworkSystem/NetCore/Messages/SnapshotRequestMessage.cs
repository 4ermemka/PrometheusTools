using System;

namespace Assets.Scripts.Network.NetCore
{
    [Serializable]
    public class SnapshotRequestMessage
    {
        public Guid RequestorClientId { get; set; }
        public string Reason { get; set; } = "connect";
    }
}
