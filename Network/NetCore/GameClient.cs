using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Base;
using Assets.Shared.ChangeDetector.Base.Mapping;
using Assets.Shared.Model;
using Assets.Shared.Network.NetCore;
using Assets.Shared.Network.NetCore.Messages;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// Клиент, который:
    /// - держит ссылку на общий WorldData (модель мира),
    /// - слушает локальные изменения (Changed) и шлёт патчи на сервер,
    /// - принимает патчи/снапшоты с сервера и применяет их к WorldData.
    /// WorldData сам по себе ничего не знает о сети и визуале.
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        private readonly ITransport _transport;
        private readonly SyncNode _worldState;          // WorldData : SyncNode
        private readonly IGameSerializer _serializer;

        // Очередь входящих патчей, применяемых на главном потоке.
        private readonly ConcurrentQueue<PatchMessage> _incomingPatches = new();

        // Очередь действий, которые нужно выполнить на главном потоке (снапшоты и т.п.).
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();

        public event Action ConnectedToHost;
        public event Action DisconnectedFromHost;
        private bool _isHost;

        public GameClient(ITransport transport, SyncNode worldState, IGameSerializer serializer)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

            _transport.Connected += OnConnected;
            _transport.Disconnected += OnDisconnected;
            _transport.DataReceived += OnDataReceived;

            // Локальные изменения WorldData → патчи на сервер
            _worldState.Changed += OnLocalWorldChanged;
        }

        /// <summary>
        /// Подключение к серверу.
        /// После успешного подключения отправляем SnapshotRequest.
        /// </summary>
        public async Task ConnectAsync(string address, int port, CancellationToken ct)
        {
            await _transport.StartAsync(address, port, ct);
            // Никаких SnapshotRequest отсюда не шлём.
        }

        public async Task RequestSnapshotAsync()
        {
            var request = new SnapshotRequestMessage
            {
                RequestorClientId = Guid.Empty // сервер сам проставит реальный id
            };

            var packet = MakePacket(MessageType.SnapshotRequest, request);

            try
            {
                await _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT] RequestSnapshotAsync failed: {ex}");
            }
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
        /// Callback TCP‑транспорта. НЕ главный поток.
        /// Здесь только раскладываем сообщения по очередям.
        /// </summary>
        private void OnDataReceived(Guid _, ArraySegment<byte> data)
        {
            var (type, payload) = ParsePacket(data);

            Debug.Log($"[CLIENT] recv packet type={type}, len={payload.Length}");

            switch (type)
            {
                case MessageType.SnapshotRequest:
                    {
                        var request = _serializer.Deserialize<SnapshotRequestMessage>(payload);
                        if (request == null) return;

                        // Важно: этот case должен быть активен только на хост-клиенте.
                        _mainThreadActions.Enqueue(() => HandleSnapshotRequest(request));
                        break;
                    }

                case MessageType.Snapshot:
                    {
                        var snapshot = _serializer.Deserialize<SnapshotMessage>(payload);
                        if (snapshot == null) return;

                        _mainThreadActions.Enqueue(() => ApplySnapshot(snapshot));
                        break;
                    }

                case MessageType.Patch:
                    {
                        var patch = _serializer.Deserialize<PatchMessage>(payload);
                        if (patch == null) return;

                        _incomingPatches.Enqueue(patch);
                        break;
                    }
            }
        }

        private async void HandleSnapshotRequest(SnapshotRequestMessage request)
        {
            try
            {
                // request.RequestorClientId – тот, кому сервер потом перешлёт Snapshot.
                var worldBytes = _serializer.Serialize(_worldState); // _worldState : WorldData : SyncNode

                var snapshot = new SnapshotMessage
                {
                    TargetClientId = request.RequestorClientId,
                    WorldDataPayload = worldBytes
                };

                var packet = MakePacket(MessageType.Snapshot, snapshot);

                // Отправляем снапшот на сервер, он по TargetClientId доставит его нужному клиенту.
                await _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);

                Debug.Log($"[CLIENT-HOST] Snapshot sent to {request.RequestorClientId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT-HOST] HandleSnapshotRequest failed: {ex}");
            }
        }

        /// <summary>
        /// Вызывается из MonoBehaviour.Update на главном потоке.
        /// Применяем все накопленные патчи и выполняем отложенные действия.
        /// </summary>
        public void Update()
        {
            // 1. Применяем входящие патчи (Position и любые другие поля WorldData)
            while (_incomingPatches.TryDequeue(out var patch))
            {
                var value = SyncValueConverter.FromDtoIfNeeded(patch.NewValue);
                Debug.Log($"[GameClient] Applying patch: {string.Join(".", patch.Path.Select(p => p.Name))} -> {patch.NewValue}");
                _worldState.ApplyPatch(patch.Path, value);
            }

            // 2. Выполняем отложенные действия (например, применение снапшота)
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
        /// Применение снапшота: полная синхронизация WorldData с авторитетной версией.
        /// </summary>
        private void ApplySnapshot(SnapshotMessage snapshot)
        {
            var newWorldData = _serializer.Deserialize<WorldData>(snapshot.WorldDataPayload);
            if (newWorldData == null)
            {
                Debug.LogWarning("[CLIENT] ApplySnapshot: deserialized WorldData is null.");
                return;
            }

            if (_worldState is not WorldData currentWorld)
            {
                Debug.LogWarning("[CLIENT] ApplySnapshot: _worldState is not WorldData.");
                return;
            }

            //Debug.Log($"[CLIENT] ApplySnapshot: {JsonConvert.SerializeObject(newWorldData)}.");
            currentWorld.ApplySnapshot(newWorldData);
            Debug.Log("[CLIENT] Snapshot applied.");
        }


        /// <summary>
        /// Локальное изменение модели (WorldData/BoxData) → отправка патча на сервер.
        /// </summary>
        private async void OnLocalWorldChanged(FieldChange change)
        {
            Debug.Log($"[CLIENT] OnLocalWorldChanged: path={string.Join(".", change.Path.Select(p => p.Name))}");

            if (_serializer == null || _transport == null)
                return;

            var path = new List<FieldPathSegment>(change.Path);
            var newValue = SyncValueConverter.ToDtoIfNeeded(change.NewValue);

            var patch = new PatchMessage
            {
                Path = path,
                NewValue = newValue
            };

            Debug.Log($"[CLIENT] MakePacket patch to send: {string.Join(".", patch.Path.Select(p => p.Name))}: {patch.NewValue}");

            ArraySegment<byte> packet;
            try
            {
                packet = MakePacket(MessageType.Patch, patch);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT] MakePacket failed: {ex}");
                return;
            }

            try
            {
                await _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT] SendAsync failed: {ex}");
            }
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