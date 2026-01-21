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
        /// Вызывается из MonoBehaviour.Update на главном потоке.
        /// Применяем все накопленные патчи и выполняем отложенные действия.
        /// </summary>
        /// <summary>
        /// Вызывается из MonoBehaviour.Update на главном потоке.
        /// Применяем все накопленные патчи и выполняем отложенные действия.
        /// </summary>
        public void Update()
        {
            // 1. Применяем входящие патчи (Position и любые другие поля WorldState)
            while (_incomingPatches.TryDequeue(out var patch))
            {
                if (patch?.ChangeData == null) continue;

                Debug.Log($"[GameClient] Applying patch: {patch.ChangeData.Path}: {patch.ChangeData.OldValue} -> {patch.ChangeData.NewValue}");

                // Ключевое изменение: передаем путь и новое значение
                // Sync<T>.SetValueSilent сам разберется с типами
                _worldState.ApplyPatch(patch.ChangeData.Path, patch.ChangeData.NewValue);
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
        /// Callback TCP‑транспорта. НЕ главный поток.
        /// Здесь только раскладываем сообщения по очередям.
        /// </summary>
        /// <summary>
        /// Callback TCP-транспорта. НЕ главный поток.
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
                        // Используем новый метод десериализации из байтов
                        var request = JsonGameSerializer.Deserialize<SnapshotRequestMessage>(payload);
                        if (request == null) return;

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
                // Получаем снапшот текущего состояния
                var snapshotDict = _worldState.CreateSnapshot();

                // Создаем сообщение со снапшотом
                var snapshot = new SnapshotMessage
                {
                    TargetClientId = request.RequestorClientId,
                    WorldDataPayload = JsonGameSerializer.Serialize(snapshotDict)
                };

                var packet = MakePacket(MessageType.Snapshot, snapshot);
                await _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);

                Debug.Log($"[CLIENT-HOST] Snapshot sent to {request.RequestorClientId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT-HOST] HandleSnapshotRequest failed: {ex}");
            }
        }

        private void ApplySnapshot(SnapshotMessage snapshot)
        {
            try
            {
                // Десериализуем словарь из JSON
                var snapshotDict = JsonGameSerializer.Deserialize<Dictionary<string, object>>(snapshot.WorldDataPayload);

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
                // Создаем ChangeData
                var change = new ChangeData
                {
                    Path = path,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Timestamp = DateTime.UtcNow.Ticks,
                    SourceClientId = Guid.Empty // Сервер заполнит
                };

                var patch = new PatchMessage
                {
                    ChangeData = change
                };

                Debug.Log($"[CLIENT] Sending patch: {path}: {oldValue} -> {newValue}");

                // Используем MakePacket с объектом, а не с byte[]
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
            // Используем новый метод сериализации в байты
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

            // Возвращаем байты, а не строку
            return Tuple.Create(type, payload);
        }
    }
}