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
    /// Authoritative network hub. The server does not own gameplay state here, but it
    /// serializes incoming patches into one ordered stream and routes snapshots.
    /// </summary>
    public sealed class GameServer : IDisposable
    {
        private readonly struct InboundPacket
        {
            public InboundPacket(Guid clientId, ArraySegment<byte> data)
            {
                ClientId = clientId;
                Data = data;
            }

            public Guid ClientId { get; }
            public ArraySegment<byte> Data { get; }
        }

        private readonly ITransport _transport;
        private readonly object _clientsLock = new object();
        private readonly object _sequenceLock = new object();
        private readonly HashSet<Guid> _clients = new HashSet<Guid>();
        private readonly ConcurrentQueue<InboundPacket> _incomingPackets = new ConcurrentQueue<InboundPacket>();
        private readonly SemaphoreSlim _queueSignal = new SemaphoreSlim(0);

        private CancellationTokenSource _serverCts;
        private Task _processorTask;
        private Guid _snapshotProviderClientId = Guid.Empty;
        private long _serverSequence;
        private bool _disposed;

        /// <summary>
        /// Echoing authoritative patches lets every client observe the same server order.
        /// Collection operations from the sender are deduplicated in GameClient.
        /// </summary>
        public bool EchoAuthoritativePatchesToSender { get; set; } = true;

        public GameServer(ITransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));

            _transport.Connected += OnClientConnected;
            _transport.Disconnected += OnClientDisconnected;
            _transport.DataReceived += OnDataReceived;
        }

        public async Task StartAsync(string address, int port, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (_serverCts != null)
                throw new InvalidOperationException("Server already started.");

            _serverCts = new CancellationTokenSource();
            _processorTask = Task.Run(() => ProcessIncomingPacketsAsync(_serverCts.Token), CancellationToken.None);

            try
            {
                await _transport.StartAsync(address, port, ct);
            }
            catch
            {
                _serverCts.Cancel();
                _queueSignal.Release();
                throw;
            }
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            if (_serverCts == null)
            {
                await _transport.StopAsync(ct);
                return;
            }

            _serverCts.Cancel();
            _queueSignal.Release();

            await _transport.StopAsync(ct);

            if (_processorTask != null)
            {
                try
                {
                    await _processorTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _serverCts.Dispose();
            _serverCts = null;
            _processorTask = null;

            lock (_clientsLock)
            {
                _clients.Clear();
                _snapshotProviderClientId = Guid.Empty;
            }
        }

        public Task BroadcastServerPatchAsync(PatchMessage patch, CancellationToken ct = default)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            if (patch.ChangeData == null) throw new ArgumentException("Patch.ChangeData is required.", nameof(patch));

            StampPatch(patch.ChangeData, Guid.Empty);
            return BroadcastPatchAsync(Guid.Empty, patch, ct);
        }

        private void OnClientConnected(Guid clientId)
        {
            var shouldBecomeSnapshotProvider = false;

            lock (_clientsLock)
            {
                _clients.Add(clientId);
                if (_snapshotProviderClientId == Guid.Empty)
                {
                    _snapshotProviderClientId = clientId;
                    shouldBecomeSnapshotProvider = true;
                }
            }

            Debug.Log($"[SERVER] Client connected: {clientId}");
            if (shouldBecomeSnapshotProvider)
            {
                Debug.Log($"[SERVER] Snapshot provider set to {clientId}");
            }

            _ = SendHandshakeAsync(clientId);
        }

        private void OnClientDisconnected(Guid clientId)
        {
            Guid newSnapshotProvider;
            var snapshotProviderChanged = false;

            lock (_clientsLock)
            {
                _clients.Remove(clientId);

                if (_snapshotProviderClientId == clientId)
                {
                    _snapshotProviderClientId = _clients.FirstOrDefault();
                    snapshotProviderChanged = _snapshotProviderClientId != Guid.Empty;
                }

                newSnapshotProvider = _snapshotProviderClientId;
            }

            Debug.Log($"[SERVER] Client disconnected: {clientId}");

            if (snapshotProviderChanged)
            {
                Debug.Log($"[SERVER] Snapshot provider changed to {newSnapshotProvider}");
                _ = SendHandshakeAsync(newSnapshotProvider);
            }
        }

        private void OnDataReceived(Guid clientId, ArraySegment<byte> data)
        {
            _incomingPackets.Enqueue(new InboundPacket(clientId, data));
            _queueSignal.Release();
        }

        private async Task ProcessIncomingPacketsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await _queueSignal.WaitAsync(ct);

                while (_incomingPackets.TryDequeue(out var packet))
                {
                    await ProcessInboundPacketAsync(packet, ct);
                }
            }
        }

        private async Task ProcessInboundPacketAsync(InboundPacket packet, CancellationToken ct)
        {
            if (!NetworkPacketCodec.TryUnpack(packet.Data, out var type, out var payload, out var error))
            {
                Debug.LogWarning($"[SERVER] Dropped invalid packet from {packet.ClientId}: {error}");
                return;
            }

            try
            {
                switch (type)
                {
                    case MessageType.SnapshotRequest:
                        await HandleSnapshotRequestAsync(packet.ClientId, payload, ct);
                        break;

                    case MessageType.Snapshot:
                        await HandleSnapshotAsync(packet.ClientId, payload, ct);
                        break;

                    case MessageType.Patch:
                        await HandlePatchAsync(packet.ClientId, payload, ct);
                        break;

                    default:
                        Debug.LogWarning($"[SERVER] Unsupported packet type {type} from {packet.ClientId}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SERVER] Failed to process {type} from {packet.ClientId}: {ex}");
            }
        }

        private async Task HandleSnapshotRequestAsync(Guid clientId, byte[] payload, CancellationToken ct)
        {
            var request = JsonGameSerializer.Deserialize<SnapshotRequestMessage>(payload);
            if (request == null)
            {
                Debug.LogWarning($"[SERVER] Empty SnapshotRequest from {clientId}");
                return;
            }

            request.RequestorClientId = clientId;

            Guid snapshotProvider;
            lock (_clientsLock)
            {
                snapshotProvider = _snapshotProviderClientId;
            }

            if (snapshotProvider == Guid.Empty)
            {
                Debug.LogWarning($"[SERVER] No snapshot provider for request from {clientId}");
                return;
            }

            var packet = NetworkPacketCodec.Pack(MessageType.SnapshotRequest, request);
            await _transport.SendAsync(snapshotProvider, packet, ct);
        }

        private async Task HandleSnapshotAsync(Guid clientId, byte[] payload, CancellationToken ct)
        {
            var snapshot = JsonGameSerializer.Deserialize<SnapshotMessage>(payload);
            if (snapshot == null)
            {
                Debug.LogWarning($"[SERVER] Empty Snapshot from {clientId}");
                return;
            }

            var targetId = snapshot.TargetClientId;
            if (targetId == Guid.Empty || !ContainsClient(targetId))
            {
                Debug.LogWarning($"[SERVER] Snapshot from {clientId} has invalid target {targetId}");
                return;
            }

            var packet = NetworkPacketCodec.Pack(MessageType.Snapshot, snapshot);
            await _transport.SendAsync(targetId, packet, ct);
        }

        private async Task HandlePatchAsync(Guid clientId, byte[] payload, CancellationToken ct)
        {
            var patch = JsonGameSerializer.Deserialize<PatchMessage>(payload);
            if (patch?.ChangeData == null)
            {
                Debug.LogWarning($"[SERVER] Empty Patch from {clientId}");
                return;
            }

            StampPatch(patch.ChangeData, clientId);
            Debug.Log($"[SERVER] Patch #{patch.ChangeData.ServerSequence} from {clientId}: {patch.ChangeData.Path}");

            await BroadcastPatchAsync(clientId, patch, ct);
        }

        private async Task BroadcastPatchAsync(Guid sourceClientId, PatchMessage patch, CancellationToken ct)
        {
            var packet = NetworkPacketCodec.Pack(MessageType.Patch, patch);
            var clients = GetClientsSnapshot();

            foreach (var clientId in clients)
            {
                if (!EchoAuthoritativePatchesToSender && clientId == sourceClientId)
                    continue;

                try
                {
                    await _transport.SendAsync(clientId, packet, ct);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SERVER] Failed to send patch to {clientId}: {ex.Message}");
                }
            }
        }

        private async Task SendHandshakeAsync(Guid clientId)
        {
            try
            {
                var handshake = new HandshakeMessage
                {
                    ClientId = clientId,
                    IsHost = clientId == GetSnapshotProviderClientId(),
                    ServerTime = DateTime.UtcNow.Ticks
                };

                var packet = NetworkPacketCodec.Pack(MessageType.Handshake, handshake);
                await _transport.SendAsync(clientId, packet, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SERVER] Failed to send handshake to {clientId}: {ex.Message}");
            }
        }

        private void StampPatch(ChangeData change, Guid sourceClientId)
        {
            if (change.PatchId == Guid.Empty)
            {
                change.PatchId = Guid.NewGuid();
            }

            change.SourceClientId = sourceClientId;
            change.ReceivedAtUtcTicks = DateTime.UtcNow.Ticks;

            lock (_sequenceLock)
            {
                change.ServerSequence = ++_serverSequence;
            }
        }

        private bool ContainsClient(Guid clientId)
        {
            lock (_clientsLock)
            {
                return _clients.Contains(clientId);
            }
        }

        private Guid GetSnapshotProviderClientId()
        {
            lock (_clientsLock)
            {
                return _snapshotProviderClientId;
            }
        }

        private List<Guid> GetClientsSnapshot()
        {
            lock (_clientsLock)
            {
                return _clients.ToList();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameServer));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _transport.Connected -= OnClientConnected;
            _transport.Disconnected -= OnClientDisconnected;
            _transport.DataReceived -= OnDataReceived;

            _serverCts?.Cancel();
            _queueSignal.Release();
            _transport.Dispose();
            _serverCts?.Dispose();
            _queueSignal.Dispose();
        }
    }
}
