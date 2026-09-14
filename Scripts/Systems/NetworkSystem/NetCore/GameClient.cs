using Assets.Shared.SyncSystem.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// Binds a TrackableNode state tree to a transport. Local Changed events become
    /// patches, while server messages are queued and applied from Unity's main thread.
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        private readonly ITransport _transport;
        private readonly TrackableNode _state;
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private readonly HashSet<Guid> _pendingLocalPatchIds = new HashSet<Guid>();
        private readonly object _pendingLock = new object();

        private long _clientSequence;
        private long _lastAppliedServerSequence;
        private bool _disposed;

        public event Action ConnectedToHost;
        public event Action DisconnectedFromHost;

        public Guid ClientId { get; private set; } = Guid.Empty;
        public bool IsSnapshotProvider { get; private set; }
        public bool IsConnected { get; private set; }

        public GameClient(ITransport transport, TrackableNode state)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            _transport.Connected += OnConnected;
            _transport.Disconnected += OnDisconnected;
            _transport.DataReceived += OnDataReceived;

            _state.Changed += OnLocalStateChanged;
        }

        public async Task ConnectAsync(string address, int port, CancellationToken ct)
        {
            ThrowIfDisposed();
            await _transport.StartAsync(address, port, ct);
        }

        public async Task RequestSnapshotAsync()
        {
            var request = new SnapshotRequestMessage
            {
                RequestorClientId = Guid.Empty
            };

            var packet = NetworkPacketCodec.Pack(MessageType.SnapshotRequest, request);

            try
            {
                await _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT] Snapshot request failed: {ex}");
            }
        }

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            return _transport.StopAsync(ct);
        }

        /// <summary>
        /// Must be called from MonoBehaviour.Update. All received state mutations are
        /// applied here so TrackableNode and Unity-facing code stay on the main thread.
        /// </summary>
        public void Update()
        {
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

        private void OnConnected(Guid _)
        {
            IsConnected = true;
            ConnectedToHost?.Invoke();
        }

        private void OnDisconnected(Guid _)
        {
            IsConnected = false;
            ClientId = Guid.Empty;
            IsSnapshotProvider = false;
            DisconnectedFromHost?.Invoke();
        }

        private void OnDataReceived(Guid _, ArraySegment<byte> data)
        {
            if (!NetworkPacketCodec.TryUnpack(data, out var type, out var payload, out var error))
            {
                Debug.LogWarning($"[CLIENT] Dropped invalid packet: {error}");
                return;
            }

            try
            {
                switch (type)
                {
                    case MessageType.Handshake:
                    {
                        var handshake = JsonGameSerializer.Deserialize<HandshakeMessage>(payload);
                        if (handshake != null)
                        {
                            _mainThreadActions.Enqueue(() => ApplyHandshake(handshake));
                        }
                        break;
                    }

                    case MessageType.SnapshotRequest:
                    {
                        var request = JsonGameSerializer.Deserialize<SnapshotRequestMessage>(payload);
                        if (request != null)
                        {
                            _mainThreadActions.Enqueue(() => HandleSnapshotRequest(request));
                        }
                        break;
                    }

                    case MessageType.Snapshot:
                    {
                        var snapshot = JsonGameSerializer.Deserialize<SnapshotMessage>(payload);
                        if (snapshot != null)
                        {
                            _mainThreadActions.Enqueue(() => ApplySnapshot(snapshot));
                        }
                        break;
                    }

                    case MessageType.Patch:
                    {
                        var patch = JsonGameSerializer.Deserialize<PatchMessage>(payload);
                        if (patch != null)
                        {
                            _mainThreadActions.Enqueue(() => ApplyPatch(patch));
                        }
                        break;
                    }

                    default:
                        Debug.LogWarning($"[CLIENT] Unsupported packet type {type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT] Failed to parse {type}: {ex}");
            }
        }

        private void ApplyHandshake(HandshakeMessage handshake)
        {
            ClientId = handshake.ClientId;
            IsSnapshotProvider = handshake.IsHost;
            Debug.Log($"[CLIENT] Handshake: client={ClientId}, snapshotProvider={IsSnapshotProvider}");
        }

        private void HandleSnapshotRequest(SnapshotRequestMessage request)
        {
            _ = SendSnapshotResponseAsync(request);
        }

        private async Task SendSnapshotResponseAsync(SnapshotRequestMessage request)
        {
            try
            {
                var snapshot = new SnapshotMessage
                {
                    TargetClientId = request.RequestorClientId,
                    WorldDataPayload = JsonGameSerializer.Serialize(_state.CreateSnapshot()),
                    Version = DateTime.UtcNow.Ticks.ToString()
                };

                var packet = NetworkPacketCodec.Pack(MessageType.Snapshot, snapshot);
                await _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);

                Debug.Log($"[CLIENT] Snapshot sent to {request.RequestorClientId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT] Snapshot response failed: {ex}");
            }
        }

        private void ApplySnapshot(SnapshotMessage snapshot)
        {
            try
            {
                var snapshotData = JsonGameSerializer.Deserialize<Dictionary<string, object>>(snapshot.WorldDataPayload);
                _state.ApplySnapshot(snapshotData);
                Debug.Log("[CLIENT] Snapshot applied.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT] Snapshot apply failed: {ex}");
            }
        }

        private void ApplyPatch(PatchMessage patch)
        {
            var change = patch.ChangeData;
            if (change == null || string.IsNullOrEmpty(change.Path))
                return;

            if (change.ServerSequence > 0 && change.ServerSequence <= _lastAppliedServerSequence)
            {
                Debug.LogWarning($"[CLIENT] Dropped duplicate/outdated patch #{change.ServerSequence}: {change.Path}");
                return;
            }

            var isOwnPendingPatch = TryAcknowledgeLocalPatch(change);
            if (change.ServerSequence > 0)
            {
                _lastAppliedServerSequence = change.ServerSequence;
            }

            // Index-based list operations are not idempotent. The local mutation has
            // already changed this client, so its authoritative echo is only an ack.
            if (isOwnPendingPatch && IsCollectionOperationPath(change.Path))
            {
                Debug.Log($"[CLIENT] Ack local collection patch #{change.ServerSequence}: {change.Path}");
                return;
            }

            Debug.Log($"[CLIENT] Apply patch #{change.ServerSequence}: {change.Path}");
            _state.ApplyPatch(change.Path, change.NewValue);
        }

        private void OnLocalStateChanged(string path, object oldValue, object newValue)
        {
            if (!IsConnected)
                return;

            var patchId = Guid.NewGuid();
            var change = new ChangeData
            {
                PatchId = patchId,
                Path = path,
                OldValue = oldValue,
                NewValue = newValue,
                Timestamp = DateTime.UtcNow.Ticks,
                ClientSequence = Interlocked.Increment(ref _clientSequence),
                SourceClientId = ClientId
            };

            lock (_pendingLock)
            {
                _pendingLocalPatchIds.Add(patchId);
            }

            _ = SendPatchAsync(new PatchMessage { ChangeData = change });
        }

        private async Task SendPatchAsync(PatchMessage patch)
        {
            try
            {
                var packet = NetworkPacketCodec.Pack(MessageType.Patch, patch);
                await _transport.SendAsync(Guid.Empty, packet, CancellationToken.None);
                Debug.Log($"[CLIENT] Sent patch #{patch.ChangeData.ClientSequence}: {patch.ChangeData.Path}");
            }
            catch (Exception ex)
            {
                lock (_pendingLock)
                {
                    _pendingLocalPatchIds.Remove(patch.ChangeData.PatchId);
                }

                Debug.LogError($"[CLIENT] Failed to send patch: {ex}");
            }
        }

        private bool TryAcknowledgeLocalPatch(ChangeData change)
        {
            if (change.PatchId == Guid.Empty)
                return false;

            lock (_pendingLock)
            {
                return _pendingLocalPatchIds.Remove(change.PatchId);
            }
        }

        private static bool IsCollectionOperationPath(string path)
        {
            var parts = path.Split('.');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part == "clear"
                    || part.StartsWith("add/", StringComparison.Ordinal)
                    || part.StartsWith("insert/", StringComparison.Ordinal)
                    || part.StartsWith("remove/", StringComparison.Ordinal)
                    || part.StartsWith("move/", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameClient));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _state.Changed -= OnLocalStateChanged;
            _transport.Connected -= OnConnected;
            _transport.Disconnected -= OnDisconnected;
            _transport.DataReceived -= OnDataReceived;
            _transport.Dispose();

            lock (_pendingLock)
            {
                _pendingLocalPatchIds.Clear();
            }
        }
    }
}
