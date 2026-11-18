namespace LANVC.Transport.Abstractions;

public enum TransportType
{
    Quic = 1,
    WebSocket = 2
}

public interface ITransportConnection : IAsyncDisposable
{
    TransportType Type { get; }
    bool IsConnected { get; }
    int LatencyMs { get; set; }
    string RemoteEndPointString { get; }

    ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);
    ValueTask<int> ReceiveFrameAsync(Memory<byte> buffer, CancellationToken ct = default);
    ValueTask CloseAsync(CancellationToken ct = default);

    event Action<ITransportConnection, Exception?>? OnDisconnected;
}

public interface ITransportClient
{
    TransportType Type { get; }
    Task<ITransportConnection> ConnectAsync(
        string host,
        int port,
        string? expectedCertFingerprint,
        CancellationToken ct = default);
}

public interface ITransportServer : IAsyncDisposable
{
    TransportType Type { get; }
    int Port { get; }
    bool IsRunning { get; }

    Task StartAsync(
        string listenAddress,
        int port,
        System.Security.Cryptography.X509Certificates.X509Certificate2 serverCert,
        Func<ITransportConnection, Task> clientHandler,
        CancellationToken ct = default);

    Task StopAsync();
}
