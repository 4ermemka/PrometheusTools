using Assets.Scripts.Network.NetCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network.NetTCP
{
    /// <summary>
    /// TCP host transport. It exposes complete framed packets:
    /// [type:1][payloadLength:4][payload].
    /// </summary>
    public sealed class TcpHostTransport : ITransport
    {
        public event Action<Guid> Connected;
        public event Action<Guid> Disconnected;
        public event Action<Guid, ArraySegment<byte>> DataReceived;

        private readonly ConcurrentDictionary<Guid, TcpClient> _clients = new ConcurrentDictionary<Guid, TcpClient>();
        private readonly ConcurrentDictionary<Guid, NetworkStream> _streams = new ConcurrentDictionary<Guid, NetworkStream>();
        private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sendLocks = new ConcurrentDictionary<Guid, SemaphoreSlim>();

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptLoopTask;

        public IReadOnlyCollection<Guid> Clients => _clients.Keys.ToList();

        public Task StartAsync(string address, int port, CancellationToken token = default)
        {
            if (_listener != null)
                throw new InvalidOperationException("Listener already started.");

            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            var ip = IPAddress.Parse(address);
            _listener = new TcpListener(ip, port);
            _listener.Start();

            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken token = default)
        {
            if (_listener == null)
                return;

            _cts.Cancel();

            try { _listener.Stop(); } catch { }

            if (_acceptLoopTask != null)
            {
                try { await _acceptLoopTask; } catch { }
            }

            foreach (var pair in _clients)
            {
                try { pair.Value.Close(); } catch { }
            }

            foreach (var pair in _sendLocks)
            {
                pair.Value.Dispose();
            }

            _clients.Clear();
            _streams.Clear();
            _sendLocks.Clear();

            _cts.Dispose();
            _cts = null;
            _acceptLoopTask = null;
            _listener = null;
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = null;

                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch
                {
                    if (ct.IsCancellationRequested)
                        break;

                    continue;
                }

                var clientId = Guid.NewGuid();
                _clients[clientId] = client;
                _streams[clientId] = client.GetStream();
                _sendLocks[clientId] = new SemaphoreSlim(1, 1);

                Connected?.Invoke(clientId);
                _ = Task.Run(() => ClientReceiveLoopAsync(clientId, client, ct), CancellationToken.None);
            }
        }

        private async Task ClientReceiveLoopAsync(Guid clientId, TcpClient client, CancellationToken ct)
        {
            var stream = client.GetStream();
            var headerBuffer = new byte[NetworkPacketCodec.HeaderSize];

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, headerBuffer, 0, headerBuffer.Length, ct))
                        break;

                    var length = NetworkPacketCodec.ReadLength(headerBuffer, 1);
                    if (length < 0 || length > NetworkPacketCodec.MaxPayloadBytes)
                        throw new InvalidOperationException($"Invalid payload length: {length}.");

                    var packetBuffer = new byte[NetworkPacketCodec.HeaderSize + length];
                    Buffer.BlockCopy(headerBuffer, 0, packetBuffer, 0, headerBuffer.Length);

                    if (!await ReadExactAsync(stream, packetBuffer, NetworkPacketCodec.HeaderSize, length, ct))
                        break;

                    DataReceived?.Invoke(clientId, new ArraySegment<byte>(packetBuffer));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TcpHostTransport] Receive loop failed for {clientId}: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                _streams.TryRemove(clientId, out _);

                if (_sendLocks.TryRemove(clientId, out var sendLock))
                {
                    sendLock.Dispose();
                }

                try { client.Close(); } catch { }
                Disconnected?.Invoke(clientId);
            }
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            var readTotal = 0;
            while (readTotal < count)
            {
                var read = await stream.ReadAsync(buffer, offset + readTotal, count - readTotal, ct);
                if (read == 0)
                    return false;

                readTotal += read;
            }

            return true;
        }

        public Task SendAsync(Guid clientId, ArraySegment<byte> payload, CancellationToken token = default)
        {
            return WriteToClientAsync(clientId, payload, token);
        }

        public async Task BroadcastAsync(ArraySegment<byte> payload, CancellationToken token = default)
        {
            foreach (var clientId in Clients)
            {
                await WriteToClientAsync(clientId, payload, token);
            }
        }

        public async Task BroadcastExceptAsync(Guid excludedClientId, ArraySegment<byte> payload, CancellationToken token = default)
        {
            foreach (var clientId in Clients)
            {
                if (clientId == excludedClientId)
                    continue;

                await WriteToClientAsync(clientId, payload, token);
            }
        }

        private async Task WriteToClientAsync(Guid clientId, ArraySegment<byte> payload, CancellationToken token)
        {
            if (payload.Array == null)
                return;

            if (!_streams.TryGetValue(clientId, out var stream) || !_sendLocks.TryGetValue(clientId, out var sendLock))
                return;

            await sendLock.WaitAsync(token);
            try
            {
                await stream.WriteAsync(payload.Array, payload.Offset, payload.Count, token);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TcpHostTransport] Send failed for {clientId}: {ex.Message}");
            }
            finally
            {
                try { sendLock.Release(); } catch (ObjectDisposedException) { }
            }
        }

        public void Dispose()
        {
            _ = StopAsync();
        }
    }
}
