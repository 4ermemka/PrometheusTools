using System;

namespace Assets.Scripts.Network.NetCore
{
    [Serializable]
    public class SnapshotRequestMessage
    {
        public Guid RequestorClientId { get; set; }
        public Guid RequestId { get; set; }
        public long ServerSequence { get; set; }
        public string Reason { get; set; } = "connect";
    }
}
