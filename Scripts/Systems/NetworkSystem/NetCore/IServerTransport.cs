using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.Scripts.Network.NetCore
{
    public interface IServerTransport
    {
        IReadOnlyCollection<Guid> ClientIds { get; }
        Task SendAsync(Guid clientId, ArraySegment<byte> data, CancellationToken ct);
    }
}
