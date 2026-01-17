
using System;

namespace Assets.Shared.Network.NetCore
{
    public sealed class SnapshotRequestMessage
    {
        public Guid RequestorClientId { get; set; }
    }
}