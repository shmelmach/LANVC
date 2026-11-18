using System.Net;
using LANVC.Core.Sessions;
using LANVC.Transport.Abstractions;

namespace LANVC.Server;

public sealed class ServerPeerSession : IPeerSession
{
    private readonly ITransportConnection _connection;
    private bool _disposed;

    public ushort PeerId { get; }
    public uint VirtualIp { get; }
    public string Hostname { get; }
    public IPEndPoint? RemoteEndPoint { get; }
    public DateTime ConnectedAt { get; }
    public ushort LatencyMs { get; set; } = 10;
    public bool IsActive => !_disposed && _connection.IsConnected;
    public ITransportConnection Connection => _connection;

    public ServerPeerSession(
        ushort peerId,
        uint virtualIp,
        string hostname,
        ITransportConnection connection)
    {
        PeerId = peerId;
        VirtualIp = virtualIp;
        Hostname = hostname;
        _connection = connection;
        ConnectedAt = DateTime.UtcNow;

        if (IPEndPoint.TryParse(connection.RemoteEndPointString, out var ep))
        {
            RemoteEndPoint = ep;
        }
    }

    public async ValueTask SendDataPacketAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        if (_disposed || !_connection.IsConnected) return;
        await _connection.SendFrameAsync(frame, ct).ConfigureAwait(false);
    }

    public void Close()
    {
        if (_disposed) return;
        _disposed = true;
        _ = _connection.CloseAsync();
    }
}
