using Assets.Shared.SyncSystem.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>Call Update and mutate the state on one owning thread. Network callbacks only enqueue work.</summary>
    public sealed class GameClient : IDisposable
    {
        private readonly ITransport transport;
        private readonly TrackableNode state;
        private readonly IClientIdentityStore identityStore;
        private readonly NetworkEvents actions = new NetworkEvents();
        private readonly HashSet<Guid> pending = new HashSet<Guid>();
        private readonly List<PatchMessage> buffered = new List<PatchMessage>();
        private readonly List<SnapshotRequestMessage> requests = new List<SnapshotRequestMessage>();
        private ClientIdentity identity;
        private TaskCompletionSource<bool> ready;
        private TaskCompletionSource<bool> snapshotReady;
        private TaskCompletionSource<bool> patchesAcknowledged;
        private Task attemptFinished = Task.CompletedTask;
        private CancellationTokenSource session;
        private CancellationTokenSource connectCancellation;
        private int lastDisconnectedGeneration;
        private long clientSequence;
        private long lastServerSequence;
        private int generation;
        private int connecting;
        private bool disposed;
        private volatile bool readyForChanges;
        private int connectionGeneration;
        private bool disconnectNotified = true;

        public event Action ConnectedToHost;
        public event Action DisconnectedFromHost;
        public event Action<ClientConnection> Connected;
        public event Action<ClientConnection> Reconnected;
        public event Action<ClientConnection> Disconnected;
        public event Action<ClientConnectionState> StateChanged;
        public event Action Ready;
        public event Action<bool> SnapshotProviderChanged;
        public event Action<PatchMessage> PatchReceived;
        public event Action<CommandReply> CommandReplied;
        public event Action<Exception> Error;

        public Guid ClientId => identity?.ClientId ?? Guid.Empty;
        public ClientConnection Connection { get; private set; }
        public bool IsSnapshotProvider { get; private set; }
        public bool IsConnected => Connection != null && connectionGeneration == generation
            && session != null && !session.IsCancellationRequested && State != ClientConnectionState.Disconnected;
        public bool IsReady => readyForChanges && IsConnected && State == ClientConnectionState.Ready;
        public ClientConnectionState State { get; private set; }
        // A single writer already owns its local values; delayed echoes must not roll it back.
        public bool ApplyOwnPatchEchoes { get; set; } = true;
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(15);

        public GameClient(ITransport transport, TrackableNode state, IClientIdentityStore identityStore = null)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.identityStore = identityStore ?? new MemoryClientIdentityStore();
            transport.Disconnected += OnDisconnected;
            transport.DataReceived += OnData;
            state.Changed += OnLocalChanged;
        }

        /// <summary>Completes after handshake AND initial snapshot. Keep pumping Update while awaiting.</summary>
        public async Task ConnectAsync(string address, int port, CancellationToken ct = default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameClient));
            if (IsConnected && session != null && !session.IsCancellationRequested) throw new InvalidOperationException("Client is already connected.");
            if (Interlocked.Exchange(ref connecting, 1) != 0) throw new InvalidOperationException("Connection already in progress.");
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            attemptFinished = finished.Task;
            var nextSession = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCancellation = nextSession;
            readyForChanges = false;
            int current = generation;
            try
            {
                await transport.StopAsync().ConfigureAwait(false);
                current = Interlocked.Increment(ref generation);
                session?.Dispose();
                session = nextSession;
                session.Token.ThrowIfCancellationRequested();
                ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                identity = identityStore.LoadOrCreate();
                identity.Validate();
                disconnectNotified = false;
                var attempt = current;
                Enqueue(attempt, () =>
                {
                    ResetSession();
                    SetState(ClientConnectionState.Connecting);
                });
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
                {
                    timeout.CancelAfter(ConnectionTimeout);
                    using (timeout.Token.Register(() => ready.TrySetCanceled()))
                    {
                        await transport.StartAsync(address, port, timeout.Token).ConfigureAwait(false);
                        Enqueue(attempt, () => SetState(ClientConnectionState.Handshaking));
                        await SendAsync(MessageType.ClientHello, new ClientHelloMessage
                        {
                            ProtocolVersion = NetworkProtocol.Version, ClientId = identity.ClientId,
                            ReconnectToken = identity.ReconnectToken
                        }, timeout.Token).ConfigureAwait(false);
                        await ready.Task.ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                var attempt = current;
                Enqueue(attempt, () => NetworkEvents.Raise(Error, ex));
                await transport.StopAsync().ConfigureAwait(false);
                QueueDisconnected(attempt);
                throw;
            }
            finally { Interlocked.Exchange(ref connecting, 0); finished.TrySetResult(true); }
        }

        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            readyForChanges = false;
            var finishing = attemptFinished;
            connectCancellation?.Cancel();
            session?.Cancel();
            ready?.TrySetCanceled();
            await transport.StopAsync(ct).ConfigureAwait(false);
            await finishing.ConfigureAwait(false);
            var attempt = generation;
            QueueDisconnected(attempt);
        }

        public void Update()
        {
            if (disposed) return;
            actions.Drain();
            // A provider must include only acknowledged local mutations in a snapshot.
            // Deferred capture joins its current state to an exact server sequence boundary.
            if (IsReady && IsSnapshotProvider && pending.Count == 0 && requests.Count > 0)
            {
                foreach (var request in requests.ToArray())
                {
                    if (lastServerSequence < request.ServerSequence) continue;
                    requests.Remove(request);
                    var snapshot = new SnapshotMessage
                    {
                        RequestId = request.RequestId, TargetClientId = request.RequestorClientId,
                        ServerSequence = lastServerSequence,
                        WorldDataPayload = JsonGameSerializer.Serialize(state.CreateSnapshot())
                    };
                    Observe(SendAsync(MessageType.Snapshot, snapshot, session.Token), generation);
                }
            }
        }

        private void Enqueue(int attempt, Action action) => actions.Enqueue(() =>
        {
            if (!disposed && attempt == generation) action();
        });

        private void OnDisconnected(Guid _)
        {
            readyForChanges = false;
            ready?.TrySetException(new IOException("Connection closed before synchronization completed."));
            session?.Cancel();
            QueueDisconnected(generation);
        }

        private void OnData(Guid _, ArraySegment<byte> packet)
        {
            var data = packet.ToArray();
            var attempt = generation;
            Enqueue(attempt, () =>
            {
                try { Handle(new ArraySegment<byte>(data)); }
                catch (Exception ex) { Fail(ex, attempt); }
            });
        }

        private void Handle(ArraySegment<byte> packet)
        {
            if (!NetworkPacketCodec.TryUnpack(packet, out var type, out var payload, out var error))
                throw new InvalidDataException(error);
            if (type == MessageType.ConnectionRejected)
            {
                var rejection = JsonGameSerializer.Deserialize<ConnectionRejectedMessage>(payload);
                throw new InvalidOperationException(rejection?.Reason ?? "Connection rejected.");
            }
            if (type == MessageType.Handshake)
            {
                ApplyHandshake(JsonGameSerializer.Deserialize<HandshakeMessage>(payload));
                return;
            }
            if (Connection == null) throw new InvalidDataException("Received data before identity handshake.");
            switch (type)
            {
                case MessageType.SnapshotProvider:
                    IsSnapshotProvider = JsonGameSerializer.Deserialize<SnapshotProviderMessage>(payload)?.IsSnapshotProvider == true;
                    NetworkEvents.Raise(SnapshotProviderChanged, IsSnapshotProvider);
                    break;
                case MessageType.SnapshotRequest:
                    var request = JsonGameSerializer.Deserialize<SnapshotRequestMessage>(payload);
                    if (!IsSnapshotProvider || request == null || request.RequestId == Guid.Empty)
                        throw new InvalidDataException("Invalid snapshot request.");
                    requests.Add(request);
                    break;
                case MessageType.Snapshot:
                    ApplySnapshot(JsonGameSerializer.Deserialize<SnapshotMessage>(payload));
                    break;
                case MessageType.PatchAcknowledgement:
                    var ackMessage = JsonGameSerializer.Deserialize<PatchMessage>(payload);
                    var ack = ackMessage?.ChangeData;
                    if (ack == null || ack.SourceClientId != ClientId || ack.ServerSequence <= 0)
                        throw new InvalidDataException("Invalid patch acknowledgement.");
                    ApplyPatch(ackMessage);
                    break;
                case MessageType.CommandReply:
                    var reply = JsonGameSerializer.Deserialize<CommandReply>(payload);
                    if (reply == null || reply.RequestId == Guid.Empty) throw new InvalidDataException("Invalid command reply.");
                    NetworkEvents.Raise(CommandReplied, reply);
                    break;
                case MessageType.Patch:
                    var patch = JsonGameSerializer.Deserialize<PatchMessage>(payload);
                    if (patch?.ChangeData == null || string.IsNullOrWhiteSpace(patch.ChangeData.Path) || patch.ChangeData.ServerSequence <= 0)
                        throw new InvalidDataException("Invalid authoritative patch.");
                    if (!IsReady) buffered.Add(patch);
                    else ApplyPatch(patch);
                    break;
                default: throw new InvalidDataException("Unsupported server message.");
            }
        }

        private void ApplyHandshake(HandshakeMessage handshake)
        {
            if (handshake == null || handshake.ProtocolVersion != NetworkProtocol.Version
                || handshake.ClientId != ClientId || handshake.ConnectionId == Guid.Empty
                || handshake.ServerSessionId == Guid.Empty || Connection != null)
                throw new InvalidDataException("Invalid or incompatible identity handshake.");
            Connection = new ClientConnection(ClientId, handshake.ConnectionId, handshake.ServerSessionId, handshake.IsReconnect);
            connectionGeneration = generation;
            IsSnapshotProvider = handshake.IsHost;
            lastServerSequence = handshake.ServerSequence;
            SetState(ClientConnectionState.Synchronizing);
            if (!IsSnapshotProvider) Observe(RequestSnapshotAsync(), generation);
            NetworkEvents.Raise(ConnectedToHost);
            NetworkEvents.Raise(Connected, Connection);
            if (handshake.IsReconnect) NetworkEvents.Raise(Reconnected, Connection);
            if (IsSnapshotProvider) CompleteReady(Guid.Empty);

        }

        /// <summary>Request a fresh authoritative state. Local edits are not sent until it arrives.</summary>
        public Task RequestSnapshotAsync()
        {
            if (!IsConnected || IsSnapshotProvider) throw new InvalidOperationException("A connected non-provider client is required.");
            if (snapshotReady != null && !snapshotReady.Task.IsCompleted) return snapshotReady.Task;
            if (pending.Count != 0) throw new InvalidOperationException("Wait for pending local patches before resynchronizing.");
            snapshotReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            buffered.Clear();
            SetState(ClientConnectionState.Synchronizing);
            Observe(SendAsync(MessageType.SnapshotRequest, new SnapshotRequestMessage(), session.Token), generation);
            return snapshotReady.Task;
        }

        private void ApplySnapshot(SnapshotMessage snapshot)
        {
            if (IsReady || IsSnapshotProvider || snapshot == null || snapshot.TargetClientId != ClientId
                || snapshot.RequestId == Guid.Empty || snapshot.ServerSequence < lastServerSequence)
                throw new InvalidDataException("Unexpected snapshot.");
            var data = JsonGameSerializer.Deserialize<Dictionary<string, object>>(snapshot.WorldDataPayload);
            if (data == null) throw new InvalidDataException("Snapshot state is missing.");
            state.ApplySnapshot(data);
            lastServerSequence = snapshot.ServerSequence;
            foreach (var patch in buffered) ApplyPatch(patch);
            buffered.Clear();
            CompleteReady(snapshot.RequestId);
            snapshotReady?.TrySetResult(true);
        }

        private void CompleteReady(Guid requestId)
        {
            if (disposed || session.IsCancellationRequested) return;
            // Ack is submitted before any Ready observer can produce a patch.
            Observe(SendAsync(MessageType.ClientReady, new ClientReadyMessage { RequestId = requestId }, session.Token), generation);
            readyForChanges = true;
            SetState(ClientConnectionState.Ready);
            if (disposed || session.IsCancellationRequested) return;
            ready?.TrySetResult(true);
            NetworkEvents.Raise(Ready);
        }

        private void ApplyPatch(PatchMessage patch)
        {
            var change = patch.ChangeData;
            if (change.ServerSequence <= lastServerSequence) return;
            var own = pending.Remove(change.PatchId);
            if (!(own && (!ApplyOwnPatchEchoes || IsCollectionOperation(change.Path)))) state.ApplyPatch(change.Path, change.NewValue);
            lastServerSequence = change.ServerSequence;
            if (pending.Count == 0) { patchesAcknowledged?.TrySetResult(true); patchesAcknowledged = null; }
            NetworkEvents.Raise(PatchReceived, patch);
        }

        private void OnLocalChanged(string path, object oldValue, object newValue)
        {
            if (!IsReady || session.IsCancellationRequested) return;
            var change = new ChangeData
            {
                PatchId = Guid.NewGuid(), Path = path, OldValue = oldValue, NewValue = newValue,
                ClientSequence = ++clientSequence, SourceClientId = ClientId
            };
            pending.Add(change.PatchId);
            // Pack now so later mutations of a collection/item cannot change the queued payload.
            Observe(SendAsync(MessageType.Patch, new PatchMessage { ChangeData = change }, session.Token), generation);
        }

        /// <summary>Owning-thread barrier for local changes. Keep pumping Update until acknowledged.</summary>
        public Task FlushLocalChangesAsync()
        {
            if (!IsReady) throw new InvalidOperationException("Client state is not ready.");
            if (pending.Count == 0) return Task.CompletedTask;
            return (patchesAcknowledged ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }

        public Task SendCommandAsync(CommandMessage command, CancellationToken ct = default)
        {
            if (!IsReady) throw new InvalidOperationException("Client state is not ready.");
            if (command == null || command.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name))
                throw new ArgumentException("Command requires a request ID and name.");
            return SendAsync(MessageType.Command, command, ct);
        }

        /// <summary>Explicit patch send. Does not mutate local state.</summary>
        public Task SendPatchAsync(PatchMessage patch, CancellationToken ct = default)
        {
            if (!IsReady) throw new InvalidOperationException("Client state is not ready.");
            if (patch?.ChangeData == null || string.IsNullOrWhiteSpace(patch.ChangeData.Path))
                throw new ArgumentException("Patch.ChangeData.Path is required.");
            return SendAsync(MessageType.Patch, patch, ct);
        }

        private Task SendAsync<T>(MessageType type, T message, CancellationToken ct) =>
            transport.SendAsync(Guid.Empty, NetworkPacketCodec.Pack(type, message), ct);

        private async void Observe(Task operation, int attempt)
        {
            try { await operation.ConfigureAwait(false); }
            catch (Exception ex) { Enqueue(attempt, () => Fail(ex, attempt)); }
        }

        private void Fail(Exception error, int attempt)
        {
            if (State == ClientConnectionState.Disconnected && disconnectNotified) return;
            NetworkEvents.Raise(Error, error);
            ready?.TrySetException(error);
            snapshotReady?.TrySetException(error);
            ApplyDisconnected();
            Observe(transport.StopAsync(), attempt);
        }

        private void SetState(ClientConnectionState value)
        {
            if (State == value) return;
            State = value;
            NetworkEvents.Raise(StateChanged, value);
        }
        private void ResetSession()
        {
            readyForChanges = false;
            snapshotReady?.TrySetCanceled();
            snapshotReady = null;
            Connection = null;
            IsSnapshotProvider = false;
            patchesAcknowledged?.TrySetCanceled();
            patchesAcknowledged = null;
            pending.Clear();
            requests.Clear();
            buffered.Clear();
            clientSequence = 0;
            lastServerSequence = 0;
        }
        private void QueueDisconnected(int attempt)
        {
            var previous = Connection;
            // Lifecycle notifications survive reconnect, but stale actions cannot reset a newer session.
            actions.Enqueue(() => { if (!disposed) ApplyDisconnected(attempt, previous); });
        }
        private void ApplyDisconnected() => ApplyDisconnected(generation, Connection);
        private void ApplyDisconnected(int attempt, ClientConnection previous)
        {
            if (attempt == generation)
            {
                previous = Connection ?? previous;
                ResetSession();
                SetState(ClientConnectionState.Disconnected);
                disconnectNotified = true;
            }
            if (attempt <= lastDisconnectedGeneration) return;
            lastDisconnectedGeneration = attempt;
            NetworkEvents.Raise(DisconnectedFromHost);
            if (previous != null) NetworkEvents.Raise(Disconnected, previous);
        }
        private static bool IsCollectionOperation(string path) => path.Split('.').Any(part =>
            part == "clear" || part.StartsWith("add/", StringComparison.Ordinal)
            || part.StartsWith("insert/", StringComparison.Ordinal) || part.StartsWith("remove/", StringComparison.Ordinal)
            || part.StartsWith("move/", StringComparison.Ordinal));

        public void Dispose()
        {
            if (disposed) return;
            DisconnectAsync().GetAwaiter().GetResult();
            Update();
            disposed = true;
            state.Changed -= OnLocalChanged;
            transport.Disconnected -= OnDisconnected;
            transport.DataReceived -= OnData;
            transport.Dispose();
            session?.Dispose();
        }
    }
}
