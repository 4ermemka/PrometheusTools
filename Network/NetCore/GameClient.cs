using Assets.Shared.Model;
using Assets.Shared.Network.NetCore;
using Assets.Shared.SyncSystem.Core;
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
    /// - держит ссылку на общий WorldState (TrackableNode),
    /// - слушает локальные изменения (Changed) и шлёт патчи на сервер,
    /// - принимает патчи/снапшоты с сервера и применяет их к WorldState.
    /// WorldState сам по себе ничего не знает о сети и визуале.
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        private readonly ITransport _transport;
        private readonly WorldState _worldState;          // WorldState : TrackableNode

        // Очередь входящих патчей, применяемых на главном потоке.
        private readonly ConcurrentQueue<PatchMessage> _incomingPatches = new();

        // Очередь действий, которые нужно выполнить на главном потоке (снапшоты и т.п.).
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();

        public event Action ConnectedToHost;
        public event Action DisconnectedFromHost;

        public GameClient(ITransport transport, WorldState worldState)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));

            _transport.Connected += OnConnected;
            _transport.Disconnected += OnDisconnected;
            _transport.DataReceived += OnDataReceived;

            // Локальные изменения WorldState → патчи на сервер
            _worldState.Changed += OnLocalWorldChanged;
        }

        /// <summary>
        /// Подключение к серверу.
        /// После успешного подключения отправляем SnapshotRequest.
        /// </summary>
        public async Task ConnectAsync(string address, int port, CancellationToken ct)
        {
            await _transport.StartAsync(address, port, ct);
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
                        var request = JsonGameSerializer.Deserialize<SnapshotRequestMessage>(payload);
                        if (request == null) return;

                        // Важно: этот case должен быть активен только на хост-клиенте.
                        _mainThreadActions.Enqueue(() => HandleSnapshotRequest(request));
                        break;
                    }

                case MessageType.Snapshot:
                    {
                        var snapshot = JsonGameSerializer.Deserialize<SnapshotMessage>(payload);
                        if (snapshot == null) return;

                        _mainThreadActions.Enqueue(() => ApplySnapshot(snapshot));
                        break;
                    }

                case MessageType.Patch:
                    {
                        var patch = JsonGameSerializer.Deserialize<PatchMessage>(payload);
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
                // Получаем снапшот текущего состояния в виде Dictionary<string, object>
                var snapshotDict = _worldState.CreateSnapshot();

                // Создаем временный WorldState для сериализации
                var tempWorldState = new WorldState();
                tempWorldState.ApplySnapshot(snapshotDict);

                var worldBytes = JsonGameSerializer.Serialize(tempWorldState);

                var snapshot = new SnapshotMessage
                {
                    TargetClientId = request.RequestorClientId,
                    WorldDataPayload = worldBytes
                };

                var packet = MakePacket(MessageType.Snapshot, snapshot);

                // Отправляем снапшот на сервер
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
            // 1. Применяем входящие патчи
            while (_incomingPatches.TryDequeue(out var patch))
            {
                if (patch == null) continue;

                Debug.Log($"[GameClient] Applying patch: {patch.Path}: {patch.OldValue} -> {patch.NewValue}");

                // Используем новый метод ApplyPatch с путем и значением
                _worldState.ApplyPatch(patch.Path, patch.NewValue);
            }

            // 2. Выполняем отложенные действия
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
        /// Применение снапшота: полная синхронизация WorldState с авторитетной версией.
        /// Теперь WorldState использует Dictionary<string, object> для снапшотов.
        /// </summary>
        private void ApplySnapshot(SnapshotMessage snapshot)
        {
            try
            {
                // Десериализуем WorldState из сообщения
                var newWorldState = JsonGameSerializer.Deserialize<WorldState>(snapshot.WorldDataPayload);
                if (newWorldState == null)
                {
                    Debug.LogError("[CLIENT] Failed to deserialize snapshot");
                    return;
                }

                // Получаем словарь из десериализованного состояния
                var snapshotDict = newWorldState.CreateSnapshot();

                // Применяем словарь к текущему состоянию
                _worldState.ApplySnapshot(snapshotDict);

                Debug.Log("[CLIENT] Snapshot applied successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT] ApplySnapshot failed: {ex}");
            }
        }

        /// <summary>
        /// Локальное изменение WorldState → отправка патча на сервер.
        /// WorldState теперь использует string path, object oldValue, object newValue.
        /// </summary>
        private async void OnLocalWorldChanged(string path, object oldValue, object newValue)
        {
            if (_transport == null)
                return;

            try
            {
                var patch = new PatchMessage
                {
                    Path = path,
                    OldValue = oldValue,
                    NewValue = newValue
                };

                Debug.Log($"[CLIENT] Sending patch: {path}: {oldValue} -> {newValue}");

                var packet = MakePacket(MessageType.Patch, patch);
                await _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT] Failed to send patch: {ex}");
            }
        }

        private ArraySegment<byte> MakePacket<T>(MessageType type, T message)
        {
            var payload = JsonGameSerializer.Serialize(message);
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