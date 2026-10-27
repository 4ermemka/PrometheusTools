using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>Local credentials. Keep the token private; ClientId is safe to use as a gameplay key.</summary>
    public sealed class ClientIdentity
    {
        public Guid ClientId { get; set; }
        public string ReconnectToken { get; set; }

        public static ClientIdentity Create()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return new ClientIdentity { ClientId = Guid.NewGuid(), ReconnectToken = Convert.ToBase64String(bytes) };
        }

        internal void Validate()
        {
            if (ClientId == Guid.Empty || string.IsNullOrEmpty(ReconnectToken))
                throw new InvalidOperationException("Client identity is incomplete.");
            try
            {
                if (Convert.FromBase64String(ReconnectToken).Length != 32)
                    throw new FormatException();
            }
            catch (FormatException) { throw new InvalidOperationException("Invalid reconnect credential."); }
        }

        internal string TokenHash()
        {
            Validate();
            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Convert.FromBase64String(ReconnectToken)));
        }
    }

    public sealed class KnownClient
    {
        public Guid ClientId { get; }
        public DateTime FirstConnectedUtc { get; }
        public KnownClient(Guid clientId, DateTime firstConnectedUtc)
        {
            ClientId = clientId;
            FirstConnectedUtc = firstConnectedUtc;
        }
    }

    public interface IClientIdentityStore { ClientIdentity LoadOrCreate(); }

    public interface IClientRegistry
    {
        IReadOnlyCollection<KnownClient> KnownClients { get; }
        /// <summary>Persist before returning; reject an existing ID with a different token.</summary>
        KnownClient Register(ClientIdentity identity, out bool isReconnect);
    }

    public sealed class MemoryClientIdentityStore : IClientIdentityStore
    {
        private readonly ClientIdentity identity = ClientIdentity.Create();
        public ClientIdentity LoadOrCreate() => new ClientIdentity { ClientId = identity.ClientId, ReconnectToken = identity.ReconnectToken };
    }

    public class MemoryClientRegistry : IClientRegistry
    {
        protected readonly object Gate = new object();
        protected readonly Dictionary<Guid, RegistryEntry> Entries = new Dictionary<Guid, RegistryEntry>();
        protected sealed class RegistryEntry
        {
            public Guid ClientId { get; set; }
            public string TokenHash { get; set; }
            public DateTime FirstConnectedUtc { get; set; }
            public KnownClient ToPublic() => new KnownClient(ClientId, FirstConnectedUtc);
        }

        public IReadOnlyCollection<KnownClient> KnownClients
        {
            get { lock (Gate) return Entries.Values.Select(entry => entry.ToPublic()).ToArray(); }
        }

        public KnownClient Register(ClientIdentity identity, out bool isReconnect)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            var hash = identity.TokenHash();
            lock (Gate)
            {
                if (Entries.TryGetValue(identity.ClientId, out var known))
                {
                    if (!string.Equals(known.TokenHash, hash, StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("Invalid reconnect credential.");
                    isReconnect = true;
                    return known.ToPublic();
                }
                var entry = new RegistryEntry { ClientId = identity.ClientId, TokenHash = hash, FirstConnectedUtc = DateTime.UtcNow };
                // Commit storage first: a failed write must not create an accepted in-memory identity.
                Persist(Entries.Values.Concat(new[] { entry }).ToArray());
                Entries.Add(entry.ClientId, entry);
                isReconnect = false;
                return entry.ToPublic();
            }
        }

        protected virtual void Persist(RegistryEntry[] entries) { }
    }
}
