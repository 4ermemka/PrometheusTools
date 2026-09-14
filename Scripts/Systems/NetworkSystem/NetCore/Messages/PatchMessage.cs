using System;

namespace Assets.Scripts.Network.NetCore
{
    [Serializable]
    public class PatchMessage
    {
        public ChangeData ChangeData { get; set; }
    }

    [Serializable]
    public class ChangeData
    {
        public Guid PatchId { get; set; } = Guid.NewGuid();
        public string Path { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
        public long Timestamp { get; set; } = DateTime.UtcNow.Ticks;

        // ClientSequence preserves the sender's local order.
        public long ClientSequence { get; set; }

        // ServerSequence is assigned by the host server and is the authoritative order.
        public long ServerSequence { get; set; }
        public long ReceivedAtUtcTicks { get; set; }
        public Guid SourceClientId { get; set; }
    }
}
