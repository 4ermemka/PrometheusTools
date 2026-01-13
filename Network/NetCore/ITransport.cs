using System;
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

        /// <summary>Отправка уже сформированного пакета [type][len][payload].</summary>
        Task SendAsync(Guid clientId, ArraySegment<byte> payload, CancellationToken token = default);

        /// <summary>Широковещательная отправка пакета всем клиентам.</summary>
        Task BroadcastAsync(ArraySegment<byte> payload, CancellationToken token = default);
    }

}

