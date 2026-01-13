using Assets.Scripts.Network.NetCore;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.Scripts.Network.NetTCP
{
    /// <summary>
    /// TCP-транспорт для клиента.
    /// Реализует тот же length-prefixed протокол: [type:1][len:4][payload:len].
    /// </summary>
    public sealed class TcpClientTransport : ITransport
    {
        public event Action<Guid> Connected;
        public event Action<Guid> Disconnected;
        public event Action<Guid, ArraySegment<byte>> DataReceived;

        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private Task _receiveLoopTask;

        // Для клиента serverId всегда один (можно использовать Guid.Empty).
        private readonly Guid _serverId = Guid.Empty;

        public async Task StartAsync(string address, int port, CancellationToken token = default)
        {
            if (_client != null)
                throw new InvalidOperationException("Client already started.");

            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _client = new TcpClient();

            await _client.ConnectAsync(address, port);
            _stream = _client.GetStream();

            if (Connected != null)
                Connected(_serverId);

            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        }

        public async Task StopAsync(CancellationToken token = default)
        {
            if (_client == null)
                return;

            _cts.Cancel();

            try { _client.Close(); } catch { /* ignore */ }

            if (_receiveLoopTask != null)
            {
                try { await _receiveLoopTask; } catch { /* ignore */ }
            }

            _client = null;
            _stream = null;

            if (Disconnected != null)
                Disconnected(_serverId);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var headerBuffer = new byte[5];

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(_stream, headerBuffer, 0, headerBuffer.Length, ct))
                        break;

                    var type = headerBuffer[0];
                    var len = headerBuffer[1]
                              | (headerBuffer[2] << 8)
                              | (headerBuffer[3] << 16)
                              | (headerBuffer[4] << 24);

                    if (len < 0 || len > 10_000_000)
                        throw new InvalidOperationException("Invalid payload length.");

                    var payloadBuffer = new byte[1 + 4 + len];
                    Buffer.BlockCopy(headerBuffer, 0, payloadBuffer, 0, headerBuffer.Length);

                    if (!await ReadExactAsync(_stream, payloadBuffer, 5, len, ct))
                        break;

                    var segment = new ArraySegment<byte>(payloadBuffer, 0, payloadBuffer.Length);
                    if (DataReceived != null)
                        DataReceived(_serverId, segment);
                }
            }
            catch
            {
                // лог при желании
            }
            finally
            {
                if (Disconnected != null)
                    Disconnected(_serverId);
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
            if (_stream == null)
                return;

            try
            {
                await _stream.WriteAsync(payload.Array, payload.Offset, payload.Count, token);
            }
            catch
            {
                // при ошибке можно дернуть StopAsync/Disconnect
            }
        }

        public Task BroadcastAsync(ArraySegment<byte> payload, CancellationToken token = default)
        {
            // Для клиента Broadcast == Send на хост
            return SendAsync(_serverId, payload, token);
        }

        public void Dispose()
        {
            _ = StopAsync();
        }
    }

}