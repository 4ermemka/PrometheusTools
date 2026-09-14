using System;

namespace Assets.Scripts.Network.NetCore
{
    public static class NetworkPacketCodec
    {
        public const int HeaderSize = 5;
        public const int MaxPayloadBytes = 10_000_000;

        public static ArraySegment<byte> Pack<T>(MessageType type, T message)
        {
            var payload = JsonGameSerializer.SerializeToBytes(message);
            var result = new byte[HeaderSize + payload.Length];

            result[0] = (byte)type;
            WriteLength(result, 1, payload.Length);
            Buffer.BlockCopy(payload, 0, result, HeaderSize, payload.Length);

            return new ArraySegment<byte>(result);
        }

        public static bool TryUnpack(ArraySegment<byte> data, out MessageType type, out byte[] payload, out string error)
        {
            type = default;
            payload = null;
            error = null;

            if (data.Array == null)
            {
                error = "Packet buffer is null.";
                return false;
            }

            if (data.Count < HeaderSize)
            {
                error = $"Packet is shorter than header: {data.Count} bytes.";
                return false;
            }

            var array = data.Array;
            var offset = data.Offset;

            type = (MessageType)array[offset];
            var length = ReadLength(array, offset + 1);

            if (length < 0 || length > MaxPayloadBytes)
            {
                error = $"Invalid payload length: {length}.";
                return false;
            }

            if (data.Count != HeaderSize + length)
            {
                error = $"Packet size mismatch. Header says {length}, segment has {data.Count - HeaderSize}.";
                return false;
            }

            payload = new byte[length];
            Buffer.BlockCopy(array, offset + HeaderSize, payload, 0, length);
            return true;
        }

        public static int ReadLength(byte[] buffer, int offset)
        {
            return buffer[offset]
                   | (buffer[offset + 1] << 8)
                   | (buffer[offset + 2] << 16)
                   | (buffer[offset + 3] << 24);
        }

        public static void WriteLength(byte[] buffer, int offset, int length)
        {
            buffer[offset] = (byte)(length & 0xFF);
            buffer[offset + 1] = (byte)((length >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((length >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((length >> 24) & 0xFF);
        }
    }
}
