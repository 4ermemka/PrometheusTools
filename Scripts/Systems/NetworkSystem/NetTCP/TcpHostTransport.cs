using Assets.Scripts.Network.NetCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.Scripts.Network.NetTCP
{
    public sealed class TcpHostTransport : ITransport, IConnectionTransport
    {
        public event Action<Guid> Connected;
        public event Action<Guid> Disconnected;
        public event Action<Guid, ArraySegment<byte>> DataReceived;
        private readonly ConcurrentDictionary<Guid, TcpPeer> peers = new ConcurrentDictionary<Guid, TcpPeer>();
        private readonly ConcurrentDictionary<Guid, Task> receivers = new ConcurrentDictionary<Guid, Task>();
        private readonly SemaphoreSlim lifecycle = new SemaphoreSlim(1, 1);
        private TcpListener listener;
        private CancellationTokenSource lifetime;
        private Task acceptor;
        private bool disposed;
        public IReadOnlyCollection<Guid> Clients => peers.Keys.ToArray();
        public int BoundPort => (listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

        public async Task StartAsync(string address, int port, CancellationToken token = default)
        {
            await lifecycle.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (disposed) throw new ObjectDisposedException(nameof(TcpHostTransport));
                if (listener != null) throw new InvalidOperationException("Listener already started.");
                token.ThrowIfCancellationRequested();
                var candidate = new TcpListener(IPAddress.Parse(address), port);
                try { candidate.Start(); }
                catch { candidate.Stop(); throw; }
                listener = candidate;
                lifetime = new CancellationTokenSource();
                var ct = lifetime.Token;
                acceptor = Task.Run(() => AcceptAsync(candidate, ct));
            }
            finally { lifecycle.Release(); }
        }
        private async Task AcceptAsync(TcpListener source, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient socket;
                try { socket = await source.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception) when (ct.IsCancellationRequested) { break; }
                var id = Guid.NewGuid();
                var peer = new TcpPeer(socket);
                peers[id] = peer;
                Connected?.Invoke(id);
                var task = ReceiveAsync(id, peer);
                receivers[id] = task;
                _ = task.ContinueWith(_ => { receivers.TryRemove(id, out var ignored); }, TaskScheduler.Default);
            }
        }
        private async Task ReceiveAsync(Guid id, TcpPeer peer)
        {
            try { await peer.ReceiveAsync(packet => DataReceived?.Invoke(id, packet)).ConfigureAwait(false); }
            catch (Exception) { /* The lifecycle event is delivered once in finally. */ }
            finally
            {
                peers.TryRemove(id, out _);
                peer.Dispose();
                Disconnected?.Invoke(id);
            }
        }
        public async Task StopAsync(CancellationToken token = default)
        {
            await lifecycle.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (listener == null) return;
                lifetime.Cancel();
                listener.Stop();
                await acceptor.ConfigureAwait(false);
                foreach (var peer in peers.Values) peer.Dispose();
                await Task.WhenAll(receivers.Values.ToArray()).ConfigureAwait(false);
                peers.Clear();
                receivers.Clear();
                lifetime.Dispose();
                lifetime = null;
                listener = null;
                acceptor = null;
            }
            finally { lifecycle.Release(); }
        }
        public Task DisconnectAsync(Guid connectionId, CancellationToken ct = default)
        {
            if (peers.TryGetValue(connectionId, out var peer)) peer.Dispose();
            return Task.CompletedTask;
        }
        public Task SendAsync(Guid clientId, ArraySegment<byte> payload, CancellationToken token = default)
        {
            if (!peers.TryGetValue(clientId, out var peer)) throw new InvalidOperationException("Client is not connected.");
            return peer.SendAsync(payload, token);
        }
        public async Task BroadcastAsync(ArraySegment<byte> payload, CancellationToken token = default)
        {
            foreach (var id in Clients) await SendAsync(id, payload, token).ConfigureAwait(false);
        }
        public async Task BroadcastExceptAsync(Guid excludedClientId, ArraySegment<byte> payload, CancellationToken token = default)
        {
            foreach (var id in Clients)
                if (id != excludedClientId) await SendAsync(id, payload, token).ConfigureAwait(false);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopAsync().GetAwaiter().GetResult();
        }
    }
}
