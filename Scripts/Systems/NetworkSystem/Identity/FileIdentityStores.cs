using System;
using System.IO;
using System.Text;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>Single-writer JSON storage. Invalid files are preserved and reported, never reset.</summary>
    internal static class IdentityFile
    {
        public static T Read<T>(string path) => JsonGameSerializer.Deserialize<T>(File.ReadAllText(path));

        public static void Write(string path, object value)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonGameSerializer.Serialize(value));
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public sealed class FileClientIdentityStore : IClientIdentityStore
    {
        private readonly string path;
        private readonly object gate = new object();
        private sealed class Document
        {
            public int Version { get; set; }
            public ClientIdentity Identity { get; set; }
        }

        public FileClientIdentityStore(string path) { this.path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path))); }

        public ClientIdentity LoadOrCreate()
        {
            lock (gate)
            {
                if (File.Exists(path))
                {
                    var document = IdentityFile.Read<Document>(path);
                    if (document?.Version != 1 || document.Identity == null)
                        throw new InvalidDataException("Invalid client identity file: " + path);
                    document.Identity.Validate();
                    return document.Identity;
                }
                var identity = ClientIdentity.Create();
                IdentityFile.Write(path, new Document { Version = 1, Identity = identity });
                return identity;
            }
        }
    }

    public sealed class FileClientRegistry : MemoryClientRegistry
    {
        private readonly string path;
        private sealed class Document
        {
            public int Version { get; set; }
            public RegistryEntry[] Clients { get; set; }
        }

        public FileClientRegistry(string path)
        {
            this.path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            if (!File.Exists(this.path)) return;
            var document = IdentityFile.Read<Document>(this.path);
            if (document?.Version != 1 || document.Clients == null)
                throw new InvalidDataException("Invalid client registry: " + this.path);
            foreach (var entry in document.Clients)
            {
                if (entry == null || entry.ClientId == Guid.Empty || string.IsNullOrEmpty(entry.TokenHash)
                    || entry.FirstConnectedUtc == default || Entries.ContainsKey(entry.ClientId))
                    throw new InvalidDataException("Invalid client registry entry: " + this.path);
                try
                {
                    if (Convert.FromBase64String(entry.TokenHash).Length != 32) throw new FormatException();
                }
                catch (FormatException) { throw new InvalidDataException("Invalid credential hash in client registry."); }
                Entries.Add(entry.ClientId, entry);
            }
        }

        protected override void Persist(RegistryEntry[] entries) =>
            IdentityFile.Write(path, new Document { Version = 1, Clients = entries });
    }
}
