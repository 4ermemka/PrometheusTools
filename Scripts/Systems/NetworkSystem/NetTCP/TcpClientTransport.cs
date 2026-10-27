using Assets.Scripts.Network.NetCore;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.Scripts.Network.NetTCP
{
    public sealed class TcpClientTransport : ITransport
    {
        public event Action<Guid> Connected;
        public event Action<Guid> Disconnected;
        public event Action<Guid, ArraySegment<byte>> DataReceived;
        private readonly SemaphoreSlim lifecycle = new SemaphoreSlim(1, 1);
        private TcpPeer peer;
        private Task receiver;
        private bool disposed;
        public IReadOnlyCollection<Guid> Clients => peer == null ? Array.Empty<Guid>() : new[] { Guid.Empty };

        public async Task StartAsync(string address, int port, CancellationToken token = default)
        {
            await lifecycle.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (disposed) throw new ObjectDisposedException(nameof(TcpClientTransport));
                if (peer != null) throw new InvalidOperationException("Client already started; stop it before reconnecting.");
                var socket = new TcpClient();
                try
                {
                    using (token.Register(socket.Close)) await socket.ConnectAsync(address, port).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    var connection = new TcpPeer(socket);
                    peer = connection;
                    Connected?.Invoke(Guid.Empty);
                    receiver = Task.Run(() => ReceiveAsync(connection));
                }
                catch
                {
                    socket.Close();
                    token.ThrowIfCancellationRequested();
                    throw;
                }
            }
            finally { lifecycle.Release(); }
        }
        private async Task ReceiveAsync(TcpPeer connection)
        {
            try { await connection.ReceiveAsync(packet => DataReceived?.Invoke(Guid.Empty, packet)).ConfigureAwait(false); }
            catch (Exception) { /* EOF, malformed framing and socket errors all terminate this connection. */ }
            finally
            {
                connection.Dispose();
                Disconnected?.Invoke(Guid.Empty);
            }
        }
        public async Task StopAsync(CancellationToken token = default)
        {
            await lifecycle.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (peer == null) return;
                peer.Dispose();
                if (receiver != null) await receiver.ConfigureAwait(false);
                peer = null;
                receiver = null;
            }
            finally { lifecycle.Release(); }
        }
        public Task SendAsync(Guid clientId, ArraySegment<byte> payload, CancellationToken token = default)
        {
            var connection = peer;
            if (connection == null) throw new InvalidOperationException("Client is not connected.");
            return connection.SendAsync(payload, token);
        }
        public Task BroadcastAsync(ArraySegment<byte> payload, CancellationToken token = default) => SendAsync(Guid.Empty, payload, token);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopAsync().GetAwaiter().GetResult();
        }
    }
}
