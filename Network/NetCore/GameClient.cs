using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Base.Mapping;
using Assets.Shared.Model;
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
        /// Callback TCP‑транспорта. НЕ главный поток.
        /// Здесь только раскладываем сообщения по очередям.
        /// </summary>
        private void OnDataReceived(Guid _, ArraySegment<byte> data)
        {
            var (type, payload) = ParsePacket(data);

            Debug.Log($"[CLIENT] recv packet type={type}, len={payload.Length}");

            switch (type)
            {
                case MessageType.Snapshot:
                    {
                        // Десериализуем SnapshotMessage.
                        var snapshot = _serializer.Deserialize<SnapshotMessage>(payload);

                        // Применить снапшот нужно на главном потоке, поэтому откладываем действие.
                        _mainThreadActions.Enqueue(() =>
                        {
                            ApplySnapshot(snapshot);
                        });

                        break;
                    }

                case MessageType.Patch:
                    {
                        // Десериализуем патч и добавляем в очередь для применения в Update.
                        var patch = _serializer.Deserialize<PatchMessage>(payload);
                        _incomingPatches.Enqueue(patch);
                        break;
                    }
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
                Debug.Log($"[GameClient] Applying patch: {patch.Path} -> {patch.NewValue}");
                _worldState.ApplyPatchSilently(patch.Path, value);
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
        /// Применение снапшота: полная синхронизация WorldData с серверной версией.
        /// </summary>
        private void ApplySnapshot(SnapshotMessage snapshot)
        {
            // Считаем, что SnapshotMessage.WorldStatePayload содержит сериализованный WorldData.
            var newWorldData = _serializer.Deserialize<WorldData>(snapshot.WorldStatePayload);

            // Простейший способ: заменить содержимое старого WorldData данными из newWorldData.
            // Важно не менять сам объект _worldState, чтобы не ломать ссылки у ChangeDetector и BoxView.

            //if (_worldState is WorldData worldData)
            //{
            //    // Очищаем и копируем список коробок.
            //    worldData.Box.Position = newWorldData.Position;
//
            //    foreach (var box in newWorldData.Boxes)
            //    {
            //        // Можно либо копировать объекты, либо класть их как есть.
            //        // Для простоты: создаём новые, чтобы избежать неожиданных ссылок.
            //        var copy = new BoxData
            //        {
            //            Position = box.Position
            //            // здесь же копировать остальные поля, если появятся
            //        };
            //        worldData.Boxes.Add(copy);
            //    }

                // После такого обновления ChangeDetector сам поднимет Changed для затронутых полей,
                // а BoxView в LateUpdate/Update подтянет позиции.
            //}
            //else
            //{
            //    Debug.LogWarning("[CLIENT] ApplySnapshot: _worldState не является WorldData.");
            //}
        }

        /// <summary>
        /// Локальное изменение модели (WorldData/BoxData) → отправка патча на сервер.
        /// </summary>
        private async void OnLocalWorldChanged(FieldChange change)
        {
            Debug.Log($"[CLIENT] OnLocalWorldChanged: path={string.Join(".", change.Path.Select(p => p.Name))} ");
            if (_serializer == null || _transport == null)
            
                return;

            var path = new List<FieldPathSegment>(change.Path);
            var newValue = SyncValueConverter.ToDtoIfNeeded(change.NewValue);

            var patch = new PatchMessage
            {
                Path = path,
                NewValue = newValue
            };

            Debug.Log($"[CLIENT] MakePacket patch to send: {patch.Path}: {patch.NewValue}");

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
