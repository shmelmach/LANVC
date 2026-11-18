using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using LANVC.Security.Certificates;
using LANVC.Transport.Abstractions;

namespace LANVC.Transport.WebSocketTransport;

public sealed class WebSocketTransportConnection : ITransportConnection
{
    private readonly WebSocket _socket;
    private readonly string _remoteEndPointString;
    private bool _disposed;

    public TransportType Type => TransportType.WebSocket;
    public bool IsConnected => !_disposed && _socket.State == WebSocketState.Open;
    public int LatencyMs { get; set; } = 15;
    public string RemoteEndPointString => _remoteEndPointString;

    public event Action<ITransportConnection, Exception?>? OnDisconnected;

    public WebSocketTransportConnection(WebSocket socket, string remoteEndPointString)
    {
        _socket = socket;
        _remoteEndPointString = remoteEndPointString;
    }

    public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        if (_disposed || _socket.State != WebSocketState.Open) return;

        try
        {
            await _socket.SendAsync(frame, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            HandleDisconnection(ex);
            throw;
        }
    }

    public async ValueTask<int> ReceiveFrameAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_disposed || _socket.State != WebSocketState.Open) return 0;

        try
        {
            int totalBytes = 0;
            ValueWebSocketReceiveResult result;

            do
            {
                var slice = buffer.Slice(totalBytes);
                result = await _socket.ReceiveAsync(slice, ct).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct).ConfigureAwait(false);
                    HandleDisconnection(null);
                    return 0;
                }

                totalBytes += result.Count;
            }
            while (!result.EndOfMessage && totalBytes < buffer.Length);

            return totalBytes;
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
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None).ConfigureAwait(false);
            }
            _socket.Dispose();
        }
        catch { }

        OnDisconnected?.Invoke(this, null);
    }
}

public sealed class WebSocketTransportClient : ITransportClient
{
    public TransportType Type => TransportType.WebSocket;

    public async Task<ITransportConnection> ConnectAsync(
        string host,
        int port,
        string? expectedCertFingerprint,
        CancellationToken ct = default)
    {
        var clientWs = new ClientWebSocket();

        clientWs.Options.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
        {
            if (cert == null) return false;
            var x509 = new X509Certificate2(cert);
            return CertificateManager.ValidateFingerprint(x509, expectedCertFingerprint);
        };

        clientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        string scheme = "wss";
        var uri = new Uri($"{scheme}://{host}:{port}/lanvc");

        await clientWs.ConnectAsync(uri, ct).ConfigureAwait(false);
        return new WebSocketTransportConnection(clientWs, uri.Authority);
    }
}

public sealed class WebSocketTransportServer : ITransportServer
{
    public TransportType Type => TransportType.WebSocket;
    public int Port { get; private set; }
    public bool IsRunning => _tcpListener != null;

    private TcpListener? _tcpListener;
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

        IPAddress bindIp = IPAddress.Parse(listenAddress);
        _tcpListener = new TcpListener(bindIp, port);
        _tcpListener.Server.NoDelay = true;
        _tcpListener.Start();

        _ = Task.Run(() => AcceptLoopAsync(_tcpListener, serverCert, clientHandler, _cts.Token));
        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(
        TcpListener listener,
        X509Certificate2 serverCert,
        Func<ITransportConnection, Task> clientHandler,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                tcpClient.NoDelay = true;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var networkStream = tcpClient.GetStream();
                        var sslStream = new SslStream(networkStream, false);

                        var sslOptions = new SslServerAuthenticationOptions
                        {
                            ServerCertificate = serverCert,
                            ClientCertificateRequired = false,
                            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls13
                        };

                        await sslStream.AuthenticateAsServerAsync(sslOptions, ct).ConfigureAwait(false);

                        // RFC 6455 HTTP/1.1 Upgrade Handshake
                        if (await PerformWebSocketHandshakeAsync(sslStream, ct).ConfigureAwait(false))
                        {
                            var ws = WebSocket.CreateFromStream(
                                sslStream,
                                isServer: true,
                                subProtocol: null,
                                keepAliveInterval: TimeSpan.FromSeconds(15));

                            var connection = new WebSocketTransportConnection(ws, tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown");
                            await clientHandler(connection).ConfigureAwait(false);
                        }
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

    private static async Task<bool> PerformWebSocketHandshakeAsync(Stream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[2048];
        int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        if (read <= 0) return false;

        string request = Encoding.UTF8.GetString(buffer, 0, read);
        if (!request.StartsWith("GET", StringComparison.OrdinalIgnoreCase))
            return false;

        var keyMatch = Regex.Match(request, @"Sec-WebSocket-Key:\s*(.*)", RegexOptions.IgnoreCase);
        if (!keyMatch.Success)
            return false;

        string secKey = keyMatch.Groups[1].Value.Trim();
        string acceptKey = ComputeWebSocketAccept(secKey);

        string response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

        byte[] respBytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(respBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        return true;
    }

    private static string ComputeWebSocketAccept(string secWebSocketKey)
    {
        const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        byte[] bytes = SHA1.HashData(Encoding.UTF8.GetBytes(secWebSocketKey + magic));
        return Convert.ToBase64String(bytes);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _tcpListener?.Stop();
        _tcpListener = null;
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}
