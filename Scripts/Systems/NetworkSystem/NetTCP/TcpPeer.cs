using Assets.Scripts.Network.NetCore;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.Scripts.Network.NetTCP
{
    /// <summary>Owns one socket, serialized writes and shared framing for both transport roles.</summary>
    internal sealed class TcpPeer : IDisposable
    {
        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly SemaphoreSlim writes = new SemaphoreSlim(1, 1);
        private int closed;
        public TcpPeer(TcpClient client)
        {
            this.client = client;
            client.NoDelay = true;
            stream = client.GetStream();
        }
        public async Task SendAsync(ArraySegment<byte> packet, CancellationToken ct)
        {
            if (packet.Array == null || packet.Count < NetworkPacketCodec.HeaderSize)
                throw new ArgumentException("A complete framed packet is required.", nameof(packet));
            await writes.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref closed) != 0) throw new IOException("Connection is closed.");
                await stream.WriteAsync(packet.Array, packet.Offset, packet.Count, ct).ConfigureAwait(false);
            }
            catch { Dispose(); throw; }
            finally { writes.Release(); }
        }
        public async Task ReceiveAsync(Action<ArraySegment<byte>> receive)
        {
            var header = new byte[NetworkPacketCodec.HeaderSize];
            while (await ReadExactAsync(header, 0, header.Length).ConfigureAwait(false))
            {
                var length = NetworkPacketCodec.ReadLength(header, 1);
                if (length < 0 || length > NetworkPacketCodec.MaxPayloadBytes)
                    throw new InvalidDataException("Invalid packet payload length.");
                var packet = new byte[header.Length + length];
                Buffer.BlockCopy(header, 0, packet, 0, header.Length);
                if (!await ReadExactAsync(packet, header.Length, length).ConfigureAwait(false))
                    throw new EndOfStreamException("Connection ended inside a packet.");
                receive(new ArraySegment<byte>(packet));
            }
        }
        private async Task<bool> ReadExactAsync(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                var read = await stream.ReadAsync(buffer, offset + total, count - total).ConfigureAwait(false);
                if (read == 0)
                {
                    if (total != 0) throw new EndOfStreamException("Connection ended inside a header.");
                    return false;
                }
                total += read;
            }
            return true;
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) == 0) client.Close();
            // Waiters still own the semaphore; disposing it races their finally/Release.
        }
    }
}
