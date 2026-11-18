namespace LANVC.Shared.Protocol;

public enum MessageType : byte
{
    None = 0x00,
    HandshakeRequest = 0x01,
    HandshakeResponse = 0x02,
    DataPacket = 0x03,
    PeerListUpdate = 0x04,
    Ping = 0x05,
    Pong = 0x06,
    DisconnectNotice = 0x07,
    GameDiscovery = 0x08
}

public enum HandshakeStatusCode : byte
{
    Success = 0,
    InvalidPassword = 1,
    ServerFull = 2,
    ProtocolVersionMismatch = 3,
    RateLimited = 4,
    InternalError = 5
}
