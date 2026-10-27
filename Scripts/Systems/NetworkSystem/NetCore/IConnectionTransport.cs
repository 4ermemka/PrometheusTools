using System;
using System.Threading;
using System.Threading.Tasks;
namespace Assets.Scripts.Network.NetCore
{
    /// <summary>Optional transport capability to close one rejected peer without stopping the host.</summary>
    public interface IConnectionTransport
    {
        Task DisconnectAsync(Guid connectionId, CancellationToken ct = default);
    }
    [Serializable]
    public sealed class ClientReadyMessage { public Guid RequestId { get; set; } }
}
