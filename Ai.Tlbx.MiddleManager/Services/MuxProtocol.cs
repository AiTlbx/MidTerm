using System.Buffers;

namespace Ai.Tlbx.MiddleManager.Services
{
    /// <summary>
    /// Binary protocol for multiplexed WebSocket communication.
    /// Frame format: [1 byte type][8 byte sessionId][payload]
    /// SessionId is the first 8 chars of the session GUID (already 8 chars).
    /// </summary>
    public static class MuxProtocol
    {
        public const int HeaderSize = 9; // 1 byte type + 8 bytes sessionId
        public const int MaxFrameSize = 64 * 1024;

        public const byte TypeTerminalOutput = 0x01;
        public const byte TypeTerminalInput = 0x02;
        public const byte TypeResize = 0x03;
        public const byte TypeSessionState = 0x04;

        public static byte[] CreateOutputFrame(string sessionId, ReadOnlySpan<byte> data)
        {
            var frame = new byte[HeaderSize + data.Length];
            frame[0] = TypeTerminalOutput;
            WriteSessionId(frame.AsSpan(1, 8), sessionId);
            data.CopyTo(frame.AsSpan(HeaderSize));
            return frame;
        }

        public static byte[] CreateStateFrame(string sessionId, bool created)
        {
            var frame = new byte[HeaderSize + 1];
            frame[0] = TypeSessionState;
            WriteSessionId(frame.AsSpan(1, 8), sessionId);
            frame[HeaderSize] = created ? (byte)1 : (byte)0;
            return frame;
        }

        public static bool TryParseFrame(
            ReadOnlySpan<byte> data,
            out byte type,
            out string sessionId,
            out ReadOnlySpan<byte> payload)
        {
            type = 0;
            sessionId = string.Empty;
            payload = default;

            if (data.Length < HeaderSize)
            {
                return false;
            }

            type = data[0];
            sessionId = System.Text.Encoding.ASCII.GetString(data.Slice(1, 8));
            payload = data.Slice(HeaderSize);
            return true;
        }

        public static (int cols, int rows) ParseResizePayload(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4)
            {
                return (80, 24);
            }
            var cols = BitConverter.ToUInt16(payload.Slice(0, 2));
            var rows = BitConverter.ToUInt16(payload.Slice(2, 2));
            return (cols, rows);
        }

        public static byte[] CreateResizePayload(int cols, int rows)
        {
            var payload = new byte[4];
            BitConverter.TryWriteBytes(payload.AsSpan(0, 2), (ushort)cols);
            BitConverter.TryWriteBytes(payload.AsSpan(2, 2), (ushort)rows);
            return payload;
        }

        private static void WriteSessionId(Span<byte> dest, string sessionId)
        {
            for (var i = 0; i < 8 && i < sessionId.Length; i++)
            {
                dest[i] = (byte)sessionId[i];
            }
        }
    }
}
