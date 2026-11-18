using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using LANVC.Security.Certificates;
using LANVC.Shared.Protocol;
using LANVC.Transport.Abstractions;

namespace LANVC.Transport.Quic;

public sealed class QuicTransportConnection : ITransportConnection
{
    private readonly QuicConnection _connection;
    private readonly QuicStream _stream;
    private bool _disposed;

    public TransportType Type => TransportType.Quic;
    public bool IsConnected => !_disposed;
    public int LatencyMs { get; set; } = 10;
    public string RemoteEndPointString => _connection.RemoteEndPoint.ToString();

    public event Action<ITransportConnection, Exception?>? OnDisconnected;

    public QuicTransportConnection(QuicConnection connection, QuicStream stream)
    {
        _connection = connection;
        _stream = stream;
    }

    public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        if (_disposed) return;
        try
        {
            await _stream.WriteAsync(frame, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            HandleDisconnection(ex);
            throw;
        }
    }

    public async ValueTask<int> ReceiveFrameAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_disposed) return 0;

        try
        {
            // 1. Read FrameHeader (5 bytes)
            int headerRead = 0;
            while (headerRead < FrameHeader.HeaderSize)
            {
                int read = await _stream.ReadAsync(buffer.Slice(headerRead, FrameHeader.HeaderSize - headerRead), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    HandleDisconnection(null);
                    return 0;
                }
                headerRead += read;
            }

            if (!FrameHeader.TryParse(buffer.Span.Slice(0, FrameHeader.HeaderSize), out var header))
            {
                throw new InvalidDataException("Invalid frame magic received over QUIC stream");
            }

            int totalExpected = FrameHeader.HeaderSize + header.PayloadLength;
            if (buffer.Length < totalExpected)
            {
                throw new InvalidOperationException($"Buffer too small for frame (expected {totalExpected}, got {buffer.Length})");
            }

            // 2. Read Payload
            int payloadRead = 0;
            while (payloadRead < header.PayloadLength)
            {
                int read = await _stream.ReadAsync(
                    buffer.Slice(FrameHeader.HeaderSize + payloadRead, header.PayloadLength - payloadRead),
                    ct).ConfigureAwait(false);

                if (read == 0)
                {
                    HandleDisconnection(null);
                    return 0;
                }
                payloadRead += read;
            }

            return totalExpected;
        }
        catch (Exception ex)
        {
            HandleDisconnection(ex);
            return 0;
        }
    }

    public async ValueTask CloseAsync(CancellationToken ct = default)
    {
        await DisposeAsync().ConfigureAwait(false);
    }

    private void HandleDisconnection(Exception? ex)
    {
        if (_disposed) return;
        _disposed = true;
        OnDisconnected?.Invoke(this, ex);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch { }

        OnDisconnected?.Invoke(this, null);
    }
}

public sealed class QuicTransportClient : ITransportClient
{
    public const string Alpn = "lanvc-v1";
    public TransportType Type => TransportType.Quic;

    public async Task<ITransportConnection> ConnectAsync(
        string host,
        int port,
        string? expectedCertFingerprint,
        CancellationToken ct = default)
    {
        IPAddress ip = IPAddress.TryParse(host, out var parsedIp)
            ? parsedIp
            : (await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false))[0];

        var endpoint = new IPEndPoint(ip, port);

        var clientAuthOptions = new SslClientAuthenticationOptions
        {
            ApplicationProtocols = new List<SslApplicationProtocol> { new(Alpn) },
            TargetHost = host,
            RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
            {
                if (cert == null) return false;
                var x509 = new X509Certificate2(cert);
                return CertificateManager.ValidateFingerprint(x509, expectedCertFingerprint);
            }
        };

        var quicOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = endpoint,
            ClientAuthenticationOptions = clientAuthOptions,
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0
        };

        var connection = await QuicConnection.ConnectAsync(quicOptions, ct).ConfigureAwait(false);
        var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct).ConfigureAwait(false);

        return new QuicTransportConnection(connection, stream);
    }
}

public sealed class QuicTransportServer : ITransportServer
{
    public const string Alpn = "lanvc-v1";
    public TransportType Type => TransportType.Quic;
    public int Port { get; private set; }
    public bool IsRunning => _listener != null;

    private QuicListener? _listener;
    private CancellationTokenSource? _cts;

    public async Task StartAsync(
        string listenAddress,
        int port,
        X509Certificate2 serverCert,
        Func<ITransportConnection, Task> clientHandler,
        CancellationToken ct = default)
    {
        Port = port;
        _cts = new CancellationTokenSource();

        var serverAuthOptions = new SslServerAuthenticationOptions
        {
            ApplicationProtocols = new List<SslApplicationProtocol> { new(Alpn) },
            ServerCertificate = serverCert
        };

        var listenerOptions = new QuicServerConnectionOptions
        {
            ServerAuthenticationOptions = serverAuthOptions,
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0
        };

        IPAddress bindIp = IPAddress.Parse(listenAddress);
        var endpoint = new IPEndPoint(bindIp, port);

        _listener = await QuicListener.ListenAsync(new QuicListenerOptions
        {
            ListenEndPoint = endpoint,
            ApplicationProtocols = new List<SslApplicationProtocol> { new(Alpn) },
            ConnectionOptionsCallback = (connection, sslInfo, cancellationToken) =>
                ValueTask.FromResult(listenerOptions)
        }, ct).ConfigureAwait(false);

        _ = Task.Run(() => AcceptLoopAsync(_listener, clientHandler, _cts.Token));
    }

    private async Task AcceptLoopAsync(
        QuicListener listener,
        Func<ITransportConnection, Task> clientHandler,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var connection = await listener.AcceptConnectionAsync(ct).ConfigureAwait(false);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var stream = await connection.AcceptInboundStreamAsync(ct).ConfigureAwait(false);
                        var transportConn = new QuicTransportConnection(connection, stream);
                        await clientHandler(transportConn).ConfigureAwait(false);
                    }
                    catch { }
                }, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_listener != null)
        {
            await _listener.DisposeAsync().ConfigureAwait(false);
            _listener = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}
