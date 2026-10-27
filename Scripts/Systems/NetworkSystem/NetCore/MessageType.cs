namespace Assets.Scripts.Network.NetCore
{
    public enum MessageType : byte
    {
        Handshake = 1,
        SnapshotRequest = 2,
        Snapshot = 3,
        Patch = 4,
        ClientHello = 5,
        ConnectionRejected = 6,
        SnapshotProvider = 7,
        ClientReady = 8,
        PatchAcknowledgement = 9,
        Command = 10,
        CommandReply = 11
    }
}
