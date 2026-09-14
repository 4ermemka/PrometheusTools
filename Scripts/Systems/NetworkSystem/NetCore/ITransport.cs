using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// Transport abstraction for the game protocol. Implementations may use TCP,
    /// WebSocket, relay services, or another channel, but must emit complete packets.
    /// </summary>
    public interface ITransport : IDisposable
    {
        event Action<Guid> Connected;
        event Action<Guid> Disconnected;
        event Action<Guid, ArraySegment<byte>> DataReceived;

        IReadOnlyCollection<Guid> Clients { get; }

        Task StartAsync(string address, int port, CancellationToken token = default);
        Task StopAsync(CancellationToken token = default);
        Task SendAsync(Guid clientId, ArraySegment<byte> payload, CancellationToken token = default);
        Task BroadcastAsync(ArraySegment<byte> payload, CancellationToken token = default);
    }
}
