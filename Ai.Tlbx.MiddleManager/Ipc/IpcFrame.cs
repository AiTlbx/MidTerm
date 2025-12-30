namespace Ai.Tlbx.MiddleManager.Ipc;

/// <summary>
/// Binary frame for IPC communication.
/// Format: [1 byte type][8 byte sessionId][2 byte length][payload]
/// </summary>
public readonly struct IpcFrame
{
    public const int HeaderSize = 11;
    public const int MaxPayloadSize = 64 * 1024;

    public IpcMessageType Type { get; }
    public string SessionId { get; }
    public ReadOnlyMemory<byte> Payload { get; }

    public IpcFrame(IpcMessageType type, string sessionId, ReadOnlyMemory<byte> payload)
    {
        Type = type;
        SessionId = sessionId;
        Payload = payload;
    }

    public IpcFrame(IpcMessageType type, string sessionId)
        : this(type, sessionId, ReadOnlyMemory<byte>.Empty)
    {
    }

    public IpcFrame(IpcMessageType type)
        : this(type, string.Empty, ReadOnlyMemory<byte>.Empty)
    {
    }
}

public enum IpcMessageType : byte
{
    // Terminal I/O (0x01-0x0F)
    Output = 0x01,
    Input = 0x02,
    Resize = 0x03,
    StateChange = 0x04,

    // Session management (0x10-0x1F)
    CreateSession = 0x10,
    SessionCreated = 0x11,
    CloseSession = 0x12,
    SessionClosed = 0x13,
    ListSessions = 0x14,
    SessionList = 0x15,
    GetBuffer = 0x16,
    Buffer = 0x17,

    // Control (0xF0-0xFF)
    Ping = 0xF0,
    Pong = 0xF3,
    Handshake = 0xF1,
    HandshakeAck = 0xF2,
    RestartWeb = 0xFD,
    Error = 0xFE,
    Shutdown = 0xFF,
}
