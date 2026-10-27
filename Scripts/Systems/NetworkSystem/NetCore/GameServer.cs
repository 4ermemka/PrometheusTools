using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>Ordered protocol hub. Gameplay lives in TrackableNode clients; durable identity lives in the registry.</summary>
    public sealed class GameServer : IDisposable
    {
        private sealed class Peer
        {
            public ClientConnection Connection;
            public bool Ready;
            public bool SnapshotDelivered;
            public Guid SnapshotRequest;
            public Guid SnapshotProvider;
            public long SnapshotMinimum;
        }

        private readonly ITransport transport;
        private readonly IClientRegistry registry;
        private readonly Guid? patchAuthority;
        private readonly object gate = new object();
        private readonly object admission = new object();
        private readonly Dictionary<Guid, Peer> peers = new Dictionary<Guid, Peer>();
        private readonly HashSet<Guid> connections = new HashSet<Guid>();
        private readonly ConcurrentQueue<Func<CancellationToken, Task>> incoming = new ConcurrentQueue<Func<CancellationToken, Task>>();
        private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
        private readonly SemaphoreSlim lifecycle = new SemaphoreSlim(1, 1);
        private readonly NetworkEvents events = new NetworkEvents();
        private CancellationTokenSource lifetime;
        private Task processor;
        private Guid provider;
        private long sequence;
        private volatile bool accepting;
        private bool disposed;

        public event Action<ClientConnection> ClientConnected;
        public event Action<ClientConnection> ClientReconnected;
        public event Action<ClientConnection> ClientDisconnected;
        public event Action<ClientConnection> ClientReady;
        public event Action<Guid> SnapshotProviderChanged;
        public event Action<Exception> Error;
        public event Action<ClientCommand> CommandReceived;
        public event Action Started;
        public event Action Stopped;

        public Guid SessionId { get; private set; }
        public bool IsRunning => accepting;
        public IReadOnlyCollection<KnownClient> KnownClients => registry.KnownClients;
        public IReadOnlyCollection<ClientConnection> ConnectedClients
        {
            get { lock (gate) return peers.Values.Select(p => p.Connection).ToArray(); }
        }
        public bool EchoAuthoritativePatchesToSender { get; set; } = true;

        public GameServer(ITransport transport, IClientRegistry registry = null, Guid? patchAuthority = null)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.registry = registry ?? new MemoryClientRegistry();
            this.patchAuthority = patchAuthority;
            transport.Connected += OnConnected;
            transport.Disconnected += OnDisconnected;
            transport.DataReceived += OnData;
        }

        /// <summary>Call on the owning thread (Unity Update) to dispatch all public events.</summary>
        public void Update() => events.Drain();

        public async Task StartAsync(string address, int port, CancellationToken ct = default)
        {
            await lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (disposed) throw new ObjectDisposedException(nameof(GameServer));
                if (lifetime != null) throw new InvalidOperationException("Server already started.");
                ct.ThrowIfCancellationRequested();
                SessionId = Guid.NewGuid();
                sequence = 0;
                lifetime = new CancellationTokenSource();
                var token = lifetime.Token;
                accepting = true;
                processor = Task.Run(() => ProcessAsync(token));
                try
                {
                    await transport.StartAsync(address, port, ct).ConfigureAwait(false);
                    events.Enqueue(() => NetworkEvents.Raise(Started));
                }
                catch
                {
                    lock (admission) accepting = false;
                    await transport.StopAsync().ConfigureAwait(false);
                    lifetime.Cancel();
                    await processor.ConfigureAwait(false);
                    lifetime.Dispose();
                    lifetime = null;
                    throw;
                }
            }
            finally { lifecycle.Release(); }
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            await lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (lifetime == null) return;
                lock (admission) accepting = false;
                await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
                // Transport loops are now joined. Finish queued work before clearing this session.
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                incoming.Enqueue(_ =>
                {
                    lock (gate)
                    {
                        foreach (var peer in peers.Values)
                        {
                            var connection = peer.Connection;
                            events.Enqueue(() => NetworkEvents.Raise(ClientDisconnected, connection));
                        }
                        peers.Clear();
                        connections.Clear();
                        provider = Guid.Empty;
                    }
                    done.SetResult(true);
                    return Task.CompletedTask;
                });
                signal.Release();
                await done.Task.ConfigureAwait(false);
                lifetime.Cancel();
                await processor.ConfigureAwait(false);
                lifetime.Dispose();
                lifetime = null;
                processor = null;
                events.Enqueue(() => NetworkEvents.Raise(Stopped));
            }
            finally { lifecycle.Release(); }
        }

        private bool Queue(Func<CancellationToken, Task> action)
        {
            lock (admission)
            {
                if (!accepting) return false;
                incoming.Enqueue(action);
                signal.Release();
                return true;
            }
        }
        private void OnConnected(Guid id) => Queue(_ => { lock (gate) connections.Add(id); return Task.CompletedTask; });
        private void OnDisconnected(Guid id) => Queue(ct => RemoveAsync(id, ct));
        private void OnData(Guid id, ArraySegment<byte> data)
        {
            var copy = data.ToArray();
            Queue(ct => HandleAsync(id, new ArraySegment<byte>(copy), ct));
        }

        private async Task ProcessAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    await signal.WaitAsync(ct).ConfigureAwait(false);
                    while (incoming.TryDequeue(out var action))
                    {
                        try { await action(ct).ConfigureAwait(false); }
                        catch (Exception ex) { events.Enqueue(() => NetworkEvents.Raise(Error, ex)); }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }

        private async Task HandleAsync(Guid id, ArraySegment<byte> data, CancellationToken ct)
        {
            if (!connections.Contains(id)) return;
            try
            {
                if (!NetworkPacketCodec.TryUnpack(data, out var type, out var payload, out var error))
                    throw new InvalidDataException(error);
                if (type == MessageType.ClientHello)
                {
                    await HelloAsync(id, JsonGameSerializer.Deserialize<ClientHelloMessage>(payload), ct).ConfigureAwait(false);
                    return;
                }
                if (!peers.TryGetValue(id, out var peer))
                    throw new InvalidOperationException("Protocol v3 hello is required before sending data.");
                switch (type)
                {
                    case MessageType.Command:
                        if (!peer.Ready) throw new InvalidOperationException("Client state is not ready.");
                        var command = JsonGameSerializer.Deserialize<CommandMessage>(payload);
                        if (command == null || command.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name))
                            throw new InvalidDataException("Command requires a request ID and name.");
                        var received = new ClientCommand(peer.Connection, command);
                        events.Enqueue(() => NetworkEvents.Raise(CommandReceived, received));
                        break;
                    case MessageType.Patch:
                        if (!peer.Ready) throw new InvalidOperationException("Client state is not ready.");
                        if (patchAuthority.HasValue && peer.Connection.ClientId != patchAuthority.Value)
                            throw new InvalidOperationException("Only the authority may publish world patches.");
                        var patch = JsonGameSerializer.Deserialize<PatchMessage>(payload);
                        ValidatePatch(patch);
                        Stamp(patch.ChangeData, peer.Connection.ClientId);
                        await BroadcastAsync(id, patch, ct).ConfigureAwait(false);
                        break;
                    case MessageType.SnapshotRequest:
                        await RequestSnapshotAsync(id, ct).ConfigureAwait(false);
                        break;
                    case MessageType.Snapshot:
                        await SnapshotAsync(id, JsonGameSerializer.Deserialize<SnapshotMessage>(payload), ct).ConfigureAwait(false);
                        break;
                    case MessageType.ClientReady:
                        var ready = JsonGameSerializer.Deserialize<ClientReadyMessage>(payload);
                        if (ready == null || ready.RequestId != peer.SnapshotRequest || !peer.SnapshotDelivered)
                            throw new InvalidOperationException("Unexpected snapshot acknowledgement.");
                        if (!peer.Ready)
                        {
                            peer.Ready = true;
                            if (provider == Guid.Empty && (!patchAuthority.HasValue || peer.Connection.ClientId == patchAuthority.Value))
                            {
                                provider = id;
                                events.Enqueue(() => NetworkEvents.Raise(SnapshotProviderChanged, peer.Connection.ClientId));
                                await SendAsync(id, MessageType.SnapshotProvider, new SnapshotProviderMessage { IsSnapshotProvider = true }, ct).ConfigureAwait(false);
                            }
                            events.Enqueue(() => NetworkEvents.Raise(ClientReady, peer.Connection));
                        }
                        break;
                    default: throw new InvalidDataException("Unsupported client message.");
                }
            }
            catch (Exception ex)
            {
                events.Enqueue(() => NetworkEvents.Raise(Error, ex));
                await RejectAsync(id, ex.Message, ct).ConfigureAwait(false);
            }
        }

        private async Task HelloAsync(Guid id, ClientHelloMessage hello, CancellationToken ct)
        {
            if (hello == null || hello.ProtocolVersion != NetworkProtocol.Version)
                throw new InvalidOperationException("Incompatible network protocol; version 3 is required.");
            if (patchAuthority.HasValue && provider == Guid.Empty && hello.ClientId != patchAuthority.Value)
                throw new InvalidOperationException("Host state is not ready. Please reconnect.");
            if (peers.ContainsKey(id)) throw new InvalidOperationException("Duplicate hello.");
            if (peers.Values.Any(p => p.Connection.ClientId == hello.ClientId))
                throw new InvalidOperationException("This client identity is already connected.");
            var identity = new ClientIdentity { ClientId = hello.ClientId, ReconnectToken = hello.ReconnectToken };
            registry.Register(identity, out var reconnect);
            var connection = new ClientConnection(identity.ClientId, id, SessionId, reconnect);
            lock (gate) peers.Add(id, new Peer { Connection = connection });
            if (provider == Guid.Empty && (peers.Count == 1 || patchAuthority == connection.ClientId))
            {
                provider = id;
                events.Enqueue(() => NetworkEvents.Raise(SnapshotProviderChanged, connection.ClientId));
            }
            peers[id].SnapshotDelivered = provider == id;
            await SendAsync(id, MessageType.Handshake, new HandshakeMessage
            {
                ProtocolVersion = NetworkProtocol.Version, ClientId = identity.ClientId,
                ConnectionId = id, ServerSessionId = SessionId, IsReconnect = reconnect,
                IsHost = provider == id, ServerSequence = sequence, ServerTime = DateTime.UtcNow.Ticks
            }, ct).ConfigureAwait(false);
            events.Enqueue(() =>
            {
                NetworkEvents.Raise(ClientConnected, connection);
                if (reconnect) NetworkEvents.Raise(ClientReconnected, connection);
            });
        }

        private async Task RequestSnapshotAsync(Guid id, CancellationToken ct)
        {
            if (provider == Guid.Empty || provider == id)
                throw new InvalidOperationException("No other snapshot provider is available.");
            var peer = peers[id];
            peer.Ready = false;
            peer.SnapshotDelivered = false;
            peer.SnapshotRequest = Guid.NewGuid();
            peer.SnapshotProvider = provider;
            peer.SnapshotMinimum = sequence;
            await SendAsync(provider, MessageType.SnapshotRequest, new SnapshotRequestMessage
            {
                RequestorClientId = peer.Connection.ClientId, RequestId = peer.SnapshotRequest, ServerSequence = sequence
            }, ct).ConfigureAwait(false);
        }

        private async Task SnapshotAsync(Guid source, SnapshotMessage snapshot, CancellationToken ct)
        {
            if (source != provider || snapshot == null || string.IsNullOrEmpty(snapshot.WorldDataPayload))
                throw new InvalidOperationException("Only the current provider may send snapshots.");
            var target = peers.FirstOrDefault(p => p.Value.Connection.ClientId == snapshot.TargetClientId);
            if (target.Value == null) return; // Requestor disconnected while provider captured its state.
            var peer = target.Value;
            if (snapshot.RequestId != peer.SnapshotRequest || source != peer.SnapshotProvider) return;
            if (snapshot.ServerSequence < peer.SnapshotMinimum || snapshot.ServerSequence > sequence)
                throw new InvalidOperationException("Snapshot has an invalid sequence boundary.");
            await SendAsync(target.Key, MessageType.Snapshot, snapshot, ct).ConfigureAwait(false);
            peer.SnapshotDelivered = true;
        }

        /// <summary>Reply to the exact connection, so a reconnect cannot receive a stale response.</summary>
        public Task ReplyAsync(ClientCommand command, string error = null, CancellationToken ct = default)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!Queue(async token =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    await SendAsync(command.Connection.ConnectionId, MessageType.CommandReply,
                        new CommandReply { RequestId = command.Message.RequestId, Error = error }, token).ConfigureAwait(false);
                    done.TrySetResult(true);
                }
                catch (Exception ex) { done.TrySetException(ex); }
            })) done.TrySetException(new InvalidOperationException("Server is stopping."));
            return done.Task;
        }

        public Task BroadcastServerPatchAsync(PatchMessage patch, CancellationToken ct = default)
        {
            ValidatePatch(patch);
            if (!accepting) throw new InvalidOperationException("Server is not running.");
            // Freeze mutable values at submission; server patches share the same ordering queue.
            var copy = JsonGameSerializer.Deserialize<PatchMessage>(JsonGameSerializer.Serialize(patch));
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!Queue(async token =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    Stamp(copy.ChangeData, Guid.Empty);
                    await BroadcastAsync(Guid.Empty, copy, token).ConfigureAwait(false);
                    done.TrySetResult(true);
                }
                catch (Exception ex) { done.TrySetException(ex); }
            })) done.TrySetException(new InvalidOperationException("Server is stopping."));
            return done.Task;
        }

        private static void ValidatePatch(PatchMessage patch)
        {
            if (patch?.ChangeData == null || string.IsNullOrWhiteSpace(patch.ChangeData.Path))
                throw new ArgumentException("Patch.ChangeData.Path is required.");
        }
        private void Stamp(ChangeData change, Guid source)
        {
            if (change.PatchId == Guid.Empty) change.PatchId = Guid.NewGuid();
            change.SourceClientId = source;
            change.ServerSequence = ++sequence;
            change.ReceivedAtUtcTicks = DateTime.UtcNow.Ticks;
        }
        private async Task BroadcastAsync(Guid source, PatchMessage patch, CancellationToken ct)
        {
            foreach (var id in peers.Keys.ToArray())
            {
                var type = !EchoAuthoritativePatchesToSender && id == source ? MessageType.PatchAcknowledgement : MessageType.Patch;
                try { await SendAsync(id, type, patch, ct).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    events.Enqueue(() => NetworkEvents.Raise(Error, ex));
                    await RemoveAsync(id, ct).ConfigureAwait(false);
                }
            }
        }
        private Task SendAsync<T>(Guid id, MessageType type, T message, CancellationToken ct) =>
            transport.SendAsync(id, NetworkPacketCodec.Pack(type, message), ct);

        private async Task RejectAsync(Guid id, string reason, CancellationToken ct)
        {
            try { await SendAsync(id, MessageType.ConnectionRejected, new ConnectionRejectedMessage { Reason = reason }, ct).ConfigureAwait(false); }
            finally
            {
                await RemoveAsync(id, ct).ConfigureAwait(false);
                if (transport is IConnectionTransport targeted) await targeted.DisconnectAsync(id, ct).ConfigureAwait(false);
            }
        }

        private async Task RemoveAsync(Guid id, CancellationToken ct)
        {
            Peer old;
            lock (gate)
            {
                connections.Remove(id);
                if (!peers.TryGetValue(id, out old)) return;
                peers.Remove(id);
            }
            events.Enqueue(() => NetworkEvents.Raise(ClientDisconnected, old.Connection));
            if (provider != id) return;
            provider = peers.Where(p => p.Value.Ready && (!patchAuthority.HasValue || p.Value.Connection.ClientId == patchAuthority.Value)).Select(p => p.Key).FirstOrDefault();
            var persistentId = provider == Guid.Empty ? Guid.Empty : peers[provider].Connection.ClientId;
            events.Enqueue(() => NetworkEvents.Raise(SnapshotProviderChanged, persistentId));
            if (provider != Guid.Empty)
                await SendAsync(provider, MessageType.SnapshotProvider, new SnapshotProviderMessage { IsSnapshotProvider = true }, ct).ConfigureAwait(false);
            foreach (var waiting in peers.Where(p => !p.Value.Ready && !p.Value.SnapshotDelivered).Select(p => p.Key).ToArray())
            {
                if (provider == Guid.Empty) await RejectAsync(waiting, "Snapshot provider disconnected. Please reconnect.", ct).ConfigureAwait(false);
                else await RequestSnapshotAsync(waiting, ct).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopAsync().GetAwaiter().GetResult();
            transport.Connected -= OnConnected;
            transport.Disconnected -= OnDisconnected;
            transport.DataReceived -= OnData;
            transport.Dispose();
        }
    }
}
