using Assets.Shared.Network.NetCore;
using Assets.Shared.Network.NetCore.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// Сервер-роутер: принимает сообщения от клиентов и пересылает их другим.
    /// Не хранит WorldState, не применяет патчи.
    /// Работает с Handshake, SnapshotRequest, Snapshot и Patch.
    /// </summary>
    public sealed class GameServer : IDisposable
    {
        private readonly ITransport _transport;

        // Простейшее хранение подключённых клиентов
        private readonly HashSet<Guid> _clients = new HashSet<Guid>();

        // Первый подключившийся клиент считаем авторитетным источником снапшотов
        private Guid _hostClientId = Guid.Empty;

        public GameServer(ITransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));

            _transport.Connected += OnClientConnected;
            _transport.Disconnected += OnClientDisconnected;
            _transport.DataReceived += OnDataReceived;
        }

        public Task StartAsync(string address, int port, CancellationToken ct = default)
            => _transport.StartAsync(address, port, ct);

        public Task StopAsync(CancellationToken ct = default)
            => _transport.StopAsync(ct);

        private async void OnClientConnected(Guid clientId)
        {
            _clients.Add(clientId);
            Debug.Log($"[SERVER] Client connected: {clientId}");

            if (_hostClientId == Guid.Empty)
            {
                _hostClientId = clientId;
                Debug.Log($"[SERVER] Host client set to {clientId}");
            }

            var isHost = clientId == _hostClientId;
            var handshake = new HandshakeMessage
            {
                IsHost = isHost,
                ClientId = clientId,
                ServerTime = DateTime.UtcNow.Ticks
            };

            var packet = MakePacket(MessageType.Handshake, handshake);
            await _transport.SendAsync(clientId, packet, CancellationToken.None);
        }

        private void OnClientDisconnected(Guid clientId)
        {
            _clients.Remove(clientId);
            Debug.Log($"[SERVER] Client disconnected: {clientId}");

            // Если отключился хост, назначаем нового
            if (_hostClientId == clientId)
            {
                _hostClientId = _clients.FirstOrDefault();
                Debug.Log($"[SERVER] Host client changed to {_hostClientId}");
            }
        }

        private async void OnDataReceived(Guid clientId, ArraySegment<byte> data)
        {
            var (type, payload) = ParsePacket(data);

            Debug.Log($"[SERVER] Received packet type={type} from client={clientId}");

            switch (type)
            {
                case MessageType.SnapshotRequest:
                    {
                        var request = JsonGameSerializer.Deserialize<SnapshotRequestMessage>(payload);
                        if (request == null)
                        {
                            Debug.LogError($"[SERVER] Failed to deserialize SnapshotRequest from {clientId}");
                            return;
                        }

                        request.RequestorClientId = clientId;
                        Debug.Log($"[SERVER] SnapshotRequest from {clientId}, forwarding to host {_hostClientId}");

                        if (_hostClientId != Guid.Empty && _clients.Contains(_hostClientId))
                        {
                            var packet = MakePacket(MessageType.SnapshotRequest, request);
                            await _transport.SendAsync(_hostClientId, packet, CancellationToken.None);
                        }
                        break;
                    }

                case MessageType.Snapshot:
                    {
                        var snapshot = JsonGameSerializer.Deserialize<SnapshotMessage>(payload);
                        if (snapshot == null)
                        {
                            Debug.LogError($"[SERVER] Failed to deserialize Snapshot from {clientId}");
                            return;
                        }

                        var targetId = snapshot.TargetClientId;
                        Debug.Log($"[SERVER] Snapshot for {targetId} from {clientId}");

                        if (targetId != Guid.Empty && _clients.Contains(targetId))
                        {
                            var packet = MakePacket(MessageType.Snapshot, snapshot);
                            await _transport.SendAsync(targetId, packet, CancellationToken.None);
                        }
                        break;
                    }

                case MessageType.Patch:
                    {
                        var patch = JsonGameSerializer.Deserialize<PatchMessage>(payload);
                        if (patch == null)
                        {
                            Debug.LogError($"[SERVER] Failed to deserialize Patch from {clientId}");
                            return;
                        }

                        if (patch.ChangeData != null)
                        {
                            patch.ChangeData.SourceClientId = clientId;
                        }

                        Debug.Log($"[SERVER] Patch from {clientId}: {patch.ChangeData?.Path}");
                        await BroadcastPatchExceptAsync(clientId, patch);
                        break;
                    }
            }
        }

        private async Task BroadcastPatchExceptAsync(Guid exceptClientId, PatchMessage patch)
        {
            // Сериализуем патч один раз
            var packet = MakePacket(MessageType.Patch, patch);

            // Рассылаем всем клиентам, кроме отправителя
            foreach (var clientId in _clients)
            {
                if (clientId == exceptClientId) continue;

                try
                {
                    await _transport.SendAsync(clientId, packet, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SERVER] Failed to send patch to {clientId}: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            _transport.Connected -= OnClientConnected;
            _transport.Disconnected -= OnClientDisconnected;
            _transport.DataReceived -= OnDataReceived;
            _transport.Dispose();
        }

        private ArraySegment<byte> MakePacket<T>(MessageType type, T message)
        {
            byte[] payload = JsonGameSerializer.SerializeToBytes(message);

            var result = new byte[1 + 4 + payload.Length];
            result[0] = (byte)type;

            var len = payload.Length;
            result[1] = (byte)(len & 0xFF);
            result[2] = (byte)((len >> 8) & 0xFF);
            result[3] = (byte)((len >> 16) & 0xFF);
            result[4] = (byte)((len >> 24) & 0xFF);

            System.Buffer.BlockCopy(payload, 0, result, 5, payload.Length);
            return new ArraySegment<byte>(result);
        }

        private Tuple<MessageType, byte[]> ParsePacket(ArraySegment<byte> data)
        {
            var array = data.Array;
            var offset = data.Offset;

            var type = (MessageType)array[offset];
            var len = array[offset + 1]
                      | (array[offset + 2] << 8)
                      | (array[offset + 3] << 16)
                      | (array[offset + 4] << 24);

            var payload = new byte[len];
            Buffer.BlockCopy(array, offset + 5, payload, 0, len);

            return Tuple.Create(type, payload);
        }
    }
}