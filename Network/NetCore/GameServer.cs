using Assets.Scripts.Network.NetTCP;
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
        private readonly IGameSerializer _serializer;

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
            // по новой схеме снапшот тоже берётся НЕ из сервера,
            // а из того клиента, который считается источником истины.
            // В минимальном варианте можно вообще не слать снапшот отсюда,
            // а сделать «pull-схему» (новый клиент просит у выбранного хоста).
        }

        private void OnClientDisconnected(Guid clientId)
        {
            // очистка по желанию
        }

        private async void OnDataReceived(Guid clientId, ArraySegment<byte> data)
        {
            var (type, payload) = ParsePacket(data);

            switch (type)
            {
                case MessageType.SnapshotRequest:
                    // по простой схеме сервер вообще не отвечает снапшотом,
                    // он просто пересылает запрос тому, кто является «источником состояния»
                    // или вообще не использует SnapshotRequest на этом уровне
                    break;

                case MessageType.Patch:
                    {
                        // сервер НЕ десериализует и НЕ применяет патч,
                        // он просто роутит его всем, кроме отправителя
                        var packet = new ArraySegment<byte>(data.Array, data.Offset, data.Count);

                        if (_transport is TcpHostTransport hostTransport)
                            await hostTransport.BroadcastExceptAsync(clientId, packet, CancellationToken.None);
                        else
                            await _transport.BroadcastAsync(packet, CancellationToken.None);

                        break;
                    }
            }
        }

        // MakePacket/ParsePacket можешь вынести в общий helper; серверу они почти не нужны,
        // он уже получает готовый packet [type][len][payload].

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
    }


}
