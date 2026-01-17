
using System;

namespace Assets.Shared.Network.NetCore
{
    public sealed class SnapshotRequestMessage
    {
        // Кто просит снапшот (заполняется сервером при форварде)
        public Guid RequestorClientId { get; set; }
    }
}