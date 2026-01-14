using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Base.Mapping;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network.NetCore
{
    public sealed class GameClient : IDisposable
    {
        private readonly ITransport _transport;
        private readonly SyncNode _worldState;
        private readonly IGameSerializer _serializer;

        private readonly ConcurrentQueue<PatchMessage> _incomingPatches = new();
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();

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

        public async Task ConnectAsync(string address, int port, CancellationToken ct = default)
        {
            await _transport.StartAsync(address, port, ct);
        }

        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            await _transport.StopAsync(ct);
        }

        public void Dispose()
        {
            _worldState.Changed -= OnLocalWorldChanged;

            _transport.Connected -= OnConnected;
            _transport.Disconnected -= OnDisconnected;
            _transport.DataReceived -= OnDataReceived;

            _transport.Dispose();
        }

        private void OnConnected(Guid _)
        {
            ConnectedToHost?.Invoke();
        }

        private void OnDisconnected(Guid _)
        {
            DisconnectedFromHost?.Invoke();
        }

        /// <summary>
        /// Входящие данные с транспорта. Этот колбэк вызывается НЕ на Unity main thread.
        /// Здесь только раскладываем сообщения по очередям.
        /// </summary>
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

                        // Любые операции, которые могут привести к RaiseChange → MonoBehaviour,
                        // выполняем на главном потоке через очередь действий.
                        _mainThreadActions.Enqueue(() =>
                        {
                            ApplySnapshot(snapshot);
                        });

                        break;
                    }

                case MessageType.Patch:
                    {
                        var patch = _serializer.Deserialize<PatchMessage>(payload);
                        _incomingPatches.Enqueue(patch);
                        break;
                    }
            }
        }

        /// <summary>
        /// Вызывается из MonoBehaviour.Update на главном потоке.
        /// Здесь применяем патчи и снапшоты.
        /// </summary>
        public void Update()
        {
            // 1. Применяем все накопленные патчи
            while (_incomingPatches.TryDequeue(out var patch))
            {
                var value = SyncValueConverter.FromDtoIfNeeded(patch.NewValue);
                _worldState.ApplyPatchSilently(patch.Path, value);
            }

            // 2. Выполняем все отложенные действия (снапшоты и пр.)
            while (_mainThreadActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        /// <summary>
        /// Применение снапшота к _worldState.
        /// Здесь уже главный поток, можно безопасно вызывать методы,
        /// которые в итоге приведут к RaiseChange → WorldStateMono → transform.
        /// </summary>
        private void ApplySnapshot(SnapshotMessage snapshot)
        {
            // самый простой вариант: полностью десериализовать состояние и
            // применить его к существующему _worldState.
            // Предположим, что WorldState – NetworkedSpriteState, обёрнутый SyncNode.

            var newState = _serializer.Deserialize<NetworkedSpriteState>(snapshot.WorldStatePayload);

            // Здесь два пути:
            // 1) заменить объект целиком и переобернуть SyncNode (если твой дизайн это позволяет);
            // 2) пройтись по полям и применить патчи к _worldState.
            //
            // Для текущего теста можно сделать прямое присваивание полей.
            // Допустим, у тебя только Position (и Changed сам поднимется через TrackableNode).

            if (_worldState is NetworkedSpriteState spriteState)
            {
                spriteState.Position = newState.Position;
                // если есть ещё поля — присвоить их аналогично
            }
            else
            {
                // если SyncNode оборачивает другой тип, здесь можно
                // сделать маппинг newState → _worldState через собственные методы
                Debug.LogWarning("[CLIENT] ApplySnapshot: _worldState is not NetworkedSpriteState.");
            }
        }

        /// <summary>
        /// Локальное изменение мира → формируем Patch и отправляем хосту.
        /// </summary>
        private async void OnLocalWorldChanged(FieldChange change)
        {
            var path = new List<FieldPathSegment>(change.Path);
            var newValue = SyncValueConverter.ToDtoIfNeeded(change.NewValue);

            var patch = new PatchMessage
            {
                Path = path,
                NewValue = newValue
            };

            var packet = MakePacket(MessageType.Patch, patch);
            await _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);
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
    }
}
