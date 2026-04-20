using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// Абстракция сетевого транспорта.
    /// DataReceived всегда выдаёт один логический пакет:
    /// [type:1][len:4][payload:len].
    /// </summary>
    public interface ITransport : IDisposable
    {
        event Action<Guid> Connected;
        event Action<Guid> Disconnected;
        event Action<Guid, ArraySegment<byte>> DataReceived;

        Task StartAsync(string address, int port, CancellationToken token = default);
        Task StopAsync(CancellationToken token = default);

        Task SendAsync(Guid clientId, ArraySegment<byte> payload, CancellationToken token = default);

        Task BroadcastAsync(ArraySegment<byte> payload, CancellationToken token = default);

        // Новое: список известных клиентов (для серверной реализации)
        IReadOnlyCollection<Guid> Clients { get; }
    }
}

