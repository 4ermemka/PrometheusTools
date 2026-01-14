using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Base.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// Авторитетный сервер (хост), принимающий патчи от клиентов и рассылающий их всем.
    /// Работает только с Snapshot и Patch, без "команд".
    /// </summary>
    public sealed class GameServer : IDisposable
    {
        private readonly ITransport _transport;
        private readonly SyncNode _worldState;
        private readonly IGameSerializer _serializer;

        public GameServer(ITransport transport, SyncNode worldState, IGameSerializer serializer)
        {
            _transport = transport;
            _worldState = worldState;
            _serializer = serializer;

            _transport.Connected += OnClientConnected;
            _transport.Disconnected += OnClientDisconnected;
            _transport.DataReceived += OnDataReceived;

            _worldState.Changed += OnWorldChangedFromHost;
        }

        private async void OnWorldChangedFromHost(FieldChange change)
        {
            // патч от локального хоста → всем клиентам
            var path = new List<FieldPathSegment>(change.Path);
            var newValue = SyncValueConverter.ToDtoIfNeeded(change.NewValue);

            var patch = new PatchMessage
            {
                Path = path,
                NewValue = newValue
            };

            var packet = MakePacket(MessageType.Patch, patch);
            await _transport.BroadcastAsync(packet, CancellationToken.None);
        }

        public Task StartAsync(string address, int port, CancellationToken ct = default)
            => _transport.StartAsync(address, port, ct);

        public Task StopAsync(CancellationToken ct = default)
            => _transport.StopAsync(ct);

        private async void OnClientConnected(Guid clientId)
        {
            var snapshot = CreateSnapshotMessage();
            var packet = MakePacket(MessageType.Snapshot, snapshot);
            await _transport.SendAsync(clientId, packet, CancellationToken.None);
        }

        private void OnClientDisconnected(Guid clientId)
        {
            // При необходимости: логика очистки/уведомлений
        }

        private async void OnDataReceived(Guid clientId, ArraySegment<byte> data)
        {
            var parsed = ParsePacket(data);
            var type = parsed.Item1;
            var payload = parsed.Item2;

            Debug.Log($"[SERVER] packet from {clientId}, type={type}, len={payload.Length}");

            switch (type)
            {
                case MessageType.SnapshotRequest:
                    {
                        var snapshot = CreateSnapshotMessage();
                        var packet = MakePacket(MessageType.Snapshot, snapshot);
                        await _transport.SendAsync(clientId, packet, CancellationToken.None);
                        break;
                    }
                case MessageType.Patch:
                    {
                        var patch = _serializer.Deserialize<PatchMessage>(payload); 
                        var value = SyncValueConverter.FromDtoIfNeeded(patch.NewValue);
                        var pathStr = string.Join(".", patch.Path.Select(p => p.Name));

                        Debug.Log($"[SERVER] Apply patch {pathStr}: {value}");

                        _worldState.ApplyPatchSilently(patch.Path, value);

                        var packet = MakePacket(MessageType.Patch, patch);
                        await _transport.BroadcastAsync(packet, CancellationToken.None);
                        break;
                    }
            }
        }

        private SnapshotMessage CreateSnapshotMessage()
        {
            var payload = _serializer.Serialize(_worldState); // _worldState : NetworkedSpriteState
            return new SnapshotMessage { WorldStatePayload = payload };
        }


        private ArraySegment<byte> MakePacket<T>(MessageType type, T message)
        {
            var payload = _serializer.Serialize(message);
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

        public void Dispose()
        {
            _transport.Connected -= OnClientConnected;
            _transport.Disconnected -= OnClientDisconnected;
            _transport.DataReceived -= OnDataReceived;
            _transport.Dispose();

            _worldState.Changed -= OnWorldChangedFromHost;
        }
    }

}
