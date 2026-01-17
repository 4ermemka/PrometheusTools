using Assets.Scripts.Network.NetTCP;
using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Base.Mapping;
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
        private readonly IGameSerializer _serializer;

        // Простейшее хранение подключённых клиентов
        private readonly HashSet<Guid> _clients = new HashSet<Guid>();

        // Первый подключившийся клиент считаем авторитетным источником снапшотов
        private Guid _hostClientId = Guid.Empty;

        public GameServer(ITransport transport, IGameSerializer serializer)
        {
            _transport = transport;
            _serializer = serializer;

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

            if (_hostClientId == Guid.Empty)
            {
                _hostClientId = clientId;
                Debug.Log($"[SERVER] Host client set to {clientId}");
            }

            var isHost = clientId == _hostClientId;
            var handshake = new HandshakeMessage { IsHost = isHost };
            var payload = _serializer.Serialize(handshake);
            var packet = MakePacket(MessageType.Handshake, payload);

            await _transport.SendAsync(clientId, packet, CancellationToken.None);
        }

        private void OnClientDisconnected(Guid clientId)
        {
            _clients.Remove(clientId);

            Debug.Log($"[SERVER] Client disconnected: {clientId}");

            // Если отключился хост, можно либо сбросить _hostClientId,
            // либо попытаться назначить нового (из оставшихся клиентов).
            if (_hostClientId == clientId)
            {
                _hostClientId = _clients.FirstOrDefault();
                Debug.Log($"[SERVER] Host client changed to {_hostClientId}");
            }
        }

        private async void OnDataReceived(Guid clientId, ArraySegment<byte> data)
        {
            var (type, payload) = ParsePacket(data);

            switch (type)
            {
                case MessageType.SnapshotRequest:
                    {
                        var request = _serializer.Deserialize<SnapshotRequestMessage>(payload);
                        if (request == null) return;

                        request.RequestorClientId = clientId;

                        var fwdPayload = _serializer.Serialize(request);
                        var packet = MakePacket(MessageType.SnapshotRequest, fwdPayload);

                        if (_hostClientId != Guid.Empty && _clients.Contains(_hostClientId))
                            await _transport.SendAsync(_hostClientId, packet, CancellationToken.None);

                        break;
                    }

                case MessageType.Snapshot:
                    {
                        var snapshot = _serializer.Deserialize<SnapshotMessage>(payload);
                        if (snapshot == null) return;

                        var targetId = snapshot.TargetClientId;
                        if (targetId != Guid.Empty && _clients.Contains(targetId))
                        {
                            var packet = MakePacket(MessageType.Snapshot, payload);
                            await _transport.SendAsync(targetId, packet, CancellationToken.None);
                        }

                        break;
                    }

                case MessageType.Patch:
                    {
                        // Патчи сервером не применяются, только рассылаются всем, кроме отправителя.
                        var packet = new ArraySegment<byte>(data.Array, data.Offset, data.Count);

                        if (_transport is TcpHostTransport hostTransport)
                            await hostTransport.BroadcastExceptAsync(clientId, packet, CancellationToken.None);
                        else
                            await _transport.BroadcastAsync(packet, CancellationToken.None);

                        break;
                    }

                case MessageType.Handshake:
                    {
                        // При необходимости можно что-то сделать с Handshake,
                        // пока просто игнорируем или логируем.
                        Debug.Log($"[SERVER] Handshake from {clientId}");
                        break;
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

        private ArraySegment<byte> MakePacket(MessageType type, byte[] payload)
        {
            var result = new byte[1 + 4 + payload.Length];
            result[0] = (byte)type;

            var len = payload.Length;
            result[1] = (byte)(len & 0xFF);
            result[2] = (byte)((len >> 8) & 0xFF);
            result[3] = (byte)((len >> 16) & 0xFF);
            result[4] = (byte)((len >> 24) & 0xFF);

            Buffer.BlockCopy(payload, 0, result, 5, payload.Length);

            return new ArraySegment<byte>(result);
        }
    }
}
