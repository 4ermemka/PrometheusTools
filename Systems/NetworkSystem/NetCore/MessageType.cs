namespace Assets.Scripts.Network.NetCore
{
    public enum MessageType : byte
    {
        Handshake = 1,
        SnapshotRequest = 2,
        Snapshot = 3,
        Patch = 4
    }
}