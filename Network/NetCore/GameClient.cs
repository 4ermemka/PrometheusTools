using Assets.Shared.ChangeDetector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// Клиент, который держит локальный WorldState и обменивается патчами с хостом.
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        private readonly ITransport _transport;
        private readonly SyncNode _worldState;
        private readonly IGameSerializer _serializer;

        public event Action ConnectedToHost;
        public event Action DisconnectedFromHost;

        public GameClient(ITransport transport, SyncNode worldState, IGameSerializer serializer)
        {
            _transport = transport;
            _worldState = worldState;
            _serializer = serializer;

            _transport.Connected += OnConnected;
            _transport.Disconnected += OnDisconnected;
            _transport.DataReceived += OnDataReceived;

            _worldState.Changed += OnLocalWorldChanged;
        }

        public Task ConnectAsync(string host, int port, CancellationToken ct = default)
            => _transport.StartAsync(host, port, ct);

        public Task DisconnectAsync(CancellationToken ct = default)
            => _transport.StopAsync(ct);

        private void OnConnected(Guid _)
        {
            ConnectedToHost?.Invoke();

            var empty = Array.Empty<byte>();
            var packet = MakePacket(MessageType.SnapshotRequest, empty);
            _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);
        }

        private void OnDisconnected(Guid _)
        {
            if (DisconnectedFromHost != null)
                DisconnectedFromHost();
        }

        private async void OnLocalWorldChanged(FieldChange change)
        {
            var patch = new PatchMessage
            {
                Path = new List<FieldPathSegment>(change.Path),
                NewValue = change.NewValue
            };
            var packet = MakePacket(MessageType.Patch, patch);
            await _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);
        }

        private void OnDataReceived(Guid _, ArraySegment<byte> data)
        {
            var parsed = ParsePacket(data);
            var type = parsed.Item1;
            var payload = parsed.Item2;

            Debug.Log($"[CLIENT] recv packet type={type}, len={payload.Length}");

            switch (type)
            {
                case MessageType.Snapshot:
                    {
                        var snapshot = _serializer.Deserialize<SnapshotMessage>(payload);
                        ApplySnapshot(snapshot);
                        break;
                    }
                case MessageType.Patch:
                    {
                        var patch = _serializer.Deserialize<PatchMessage>(payload);
                        var pathStr = string.Join(".", patch.Path.Select(p => p.Name));
                        Debug.Log($"[CLIENT] Apply patch {pathStr}: {patch.NewValue}");
                        _worldState.ApplyPatchSilently(patch.Path, patch.NewValue);
                        break;
                    }
            }
        }

        private void ApplySnapshot(SnapshotMessage snapshot)
        {
            // десериализуем ту же модель, с которой работаем (_worldState : SyncNode)
            var fresh = _serializer.Deserialize<NetworkedSpriteState>(snapshot.WorldStatePayload);

            // простой вариант для отладки — применить полные патчи к текущему _worldState:
            // 1) Position
            _worldState.ApplyPatchSilently(
                new List<FieldPathSegment> { new FieldPathSegment(nameof(NetworkedSpriteState.Position)) },
                fresh.Position
            );

            // при более сложном WorldState сюда добавишь остальные поля
        }

        private ArraySegment<byte> MakePacket<T>(MessageType type, T messageOrEmpty)
        {
            byte[] payload;

            if (messageOrEmpty is byte[])
                payload = (byte[])(object)messageOrEmpty;
            else
                payload = _serializer.Serialize(messageOrEmpty);

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
            _worldState.Changed -= OnLocalWorldChanged;
            _transport.Connected -= OnConnected;
            _transport.Disconnected -= OnDisconnected;
            _transport.DataReceived -= OnDataReceived;
            _transport.Dispose();
        }
    }

}
