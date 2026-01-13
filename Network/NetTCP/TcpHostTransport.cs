using Assets.Scripts.Network.NetCore;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.Scripts.Network.NetTCP
{
    /// <summary>
    /// TCP-транспорт для хоста. Реализует length-prefixed протокол:
    /// [type:1][len:4][payload:len].
    /// Для каждого клиента поднимается отдельный цикл чтения,
    /// который собирает целые пакеты и поднимает DataReceived.
    /// </summary>
    public sealed class TcpHostTransport : ITransport
    {
        public event Action<Guid> Connected;
        public event Action<Guid> Disconnected;
        public event Action<Guid, ArraySegment<byte>> DataReceived;

        private readonly ConcurrentDictionary<Guid, TcpClient> _clients =
            new ConcurrentDictionary<Guid, TcpClient>();

        private readonly ConcurrentDictionary<Guid, NetworkStream> _streams =
            new ConcurrentDictionary<Guid, NetworkStream>();

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptLoopTask;

        public Task StartAsync(string address, int port, CancellationToken token = default)
        {
            if (_listener != null)
                throw new InvalidOperationException("Listener already started.");

            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            var ip = IPAddress.Parse(address);
            _listener = new TcpListener(ip, port);
            _listener.Start();

            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken token = default)
        {
            if (_listener == null)
                return;

            _cts.Cancel();

            try { _listener.Stop(); } catch { /* ignore */ }

            if (_acceptLoopTask != null)
            {
                try { await _acceptLoopTask; } catch { /* ignore */ }
            }

            foreach (var pair in _clients)
            {
                try { pair.Value.Close(); } catch { /* ignore */ }
            }

            _clients.Clear();
            _streams.Clear();
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

                if (Connected != null)
                    Connected(clientId);

                _ = Task.Run(() => ClientReceiveLoopAsync(clientId, client, ct), ct);
            }
        }

        private async Task ClientReceiveLoopAsync(Guid clientId, TcpClient client, CancellationToken ct)
        {
            var stream = client.GetStream();
            var headerBuffer = new byte[5]; // type(1) + len(4)

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // читаем заголовок
                    if (!await ReadExactAsync(stream, headerBuffer, 0, headerBuffer.Length, ct))
                        break;

                    var type = headerBuffer[0];
                    var len = headerBuffer[1]
                              | (headerBuffer[2] << 8)
                              | (headerBuffer[3] << 16)
                              | (headerBuffer[4] << 24);

                    if (len < 0 || len > 10_000_000)
                        throw new InvalidOperationException("Invalid payload length.");

                    var payloadBuffer = new byte[1 + 4 + len];
                    // копируем заголовок в общий буфер
                    Buffer.BlockCopy(headerBuffer, 0, payloadBuffer, 0, headerBuffer.Length);

                    // читаем тело
                    if (!await ReadExactAsync(stream, payloadBuffer, 5, len, ct))
                        break;

                    var segment = new ArraySegment<byte>(payloadBuffer, 0, payloadBuffer.Length);
                    if (DataReceived != null)
                        DataReceived(clientId, segment);
                }
            }
            catch
            {
                // при желании логировать
            }
            finally
            {
                TcpClient removedClient;
                _clients.TryRemove(clientId, out removedClient);
                NetworkStream removedStream;
                _streams.TryRemove(clientId, out removedStream);

                try { client.Close(); } catch { /* ignore */ }

                if (Disconnected != null)
                    Disconnected(clientId);
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
            NetworkStream stream;
            if (!_streams.TryGetValue(clientId, out stream))
                return;

            try
            {
                await stream.WriteAsync(payload.Array, payload.Offset, payload.Count, token);
            }
            catch
            {
                // опционально инициировать дисконнект
            }
        }

        public async Task BroadcastAsync(ArraySegment<byte> payload, CancellationToken token = default)
        {
            foreach (var pair in _streams)
            {
                try
                {
                    await pair.Value.WriteAsync(payload.Array, payload.Offset, payload.Count, token);
                }
                catch
                {
                    // при ошибке можно инициировать отключение конкретного клиента
                }
            }
        }

        public void Dispose()
        {
            _ = StopAsync();
        }
    }

}