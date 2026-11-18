using System.Net;

namespace LANVC.Core.Sessions;

public interface IPeerSession
{
    ushort PeerId { get; }
    uint VirtualIp { get; }
    string Hostname { get; }
    IPEndPoint? RemoteEndPoint { get; }
    DateTime ConnectedAt { get; }
    ushort LatencyMs { get; set; }
    bool IsActive { get; }

    ValueTask SendDataPacketAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);
    void Close();
}
