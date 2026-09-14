using Assets.Scripts.Network.NetCore;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network.NetTCP
{
    /// <summary>
    /// TCP client transport. The client has one remote endpoint, addressed as Guid.Empty.
    /// </summary>
    public sealed class TcpClientTransport : ITransport
    {
        public event Action<Guid> Connected;
        public event Action<Guid> Disconnected;
        public event Action<Guid, ArraySegment<byte>> DataReceived;

        private readonly Guid _serverId = Guid.Empty;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private Task _receiveLoopTask;
        private int _disconnectRaised;

        public IReadOnlyCollection<Guid> Clients { get; } = new[] { Guid.Empty };

        public async Task StartAsync(string address, int port, CancellationToken token = default)
        {
            if (_client != null)
                throw new InvalidOperationException("Client already started.");

            _disconnectRaised = 0;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _client = new TcpClient();

            await _client.ConnectAsync(address, port);
            _stream = _client.GetStream();

            Connected?.Invoke(_serverId);
            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), CancellationToken.None);
        }

        public async Task StopAsync(CancellationToken token = default)
        {
            if (_client == null)
                return;

            _cts.Cancel();

            try { _client.Close(); } catch { }

            if (_receiveLoopTask != null)
            {
                try { await _receiveLoopTask; } catch { }
            }

            _client = null;
            _stream = null;
            _receiveLoopTask = null;

            _cts.Dispose();
            _cts = null;

            RaiseDisconnectedOnce();
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var headerBuffer = new byte[NetworkPacketCodec.HeaderSize];

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(_stream, headerBuffer, 0, headerBuffer.Length, ct))
                        break;

                    var length = NetworkPacketCodec.ReadLength(headerBuffer, 1);
                    if (length < 0 || length > NetworkPacketCodec.MaxPayloadBytes)
                        throw new InvalidOperationException($"Invalid payload length: {length}.");

                    var packetBuffer = new byte[NetworkPacketCodec.HeaderSize + length];
                    Buffer.BlockCopy(headerBuffer, 0, packetBuffer, 0, headerBuffer.Length);

                    if (!await ReadExactAsync(_stream, packetBuffer, NetworkPacketCodec.HeaderSize, length, ct))
                        break;

                    DataReceived?.Invoke(_serverId, new ArraySegment<byte>(packetBuffer));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TcpClientTransport] Receive loop failed: {ex.Message}");
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    RaiseDisconnectedOnce();
                }
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

        public async Task SendAsync(Guid clientId, ArraySegment<byte> payload, CancellationToken token = default)
        {
            if (_stream == null || payload.Array == null)
                return;

            await _sendLock.WaitAsync(token);
            try
            {
                await _stream.WriteAsync(payload.Array, payload.Offset, payload.Count, token);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TcpClientTransport] Send failed: {ex.Message}");
            }
            finally
            {
                try { _sendLock.Release(); } catch (ObjectDisposedException) { }
            }
        }

        public Task BroadcastAsync(ArraySegment<byte> payload, CancellationToken token = default)
        {
            return SendAsync(_serverId, payload, token);
        }

        private void RaiseDisconnectedOnce()
        {
            if (Interlocked.Exchange(ref _disconnectRaised, 1) == 0)
            {
                Disconnected?.Invoke(_serverId);
            }
        }

        public void Dispose()
        {
            _ = StopAsync();
            _sendLock.Dispose();
        }
    }
}
