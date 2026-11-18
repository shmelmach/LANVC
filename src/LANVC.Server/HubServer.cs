using System.Buffers;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LANVC.Core.Routing;
using LANVC.Security.Argon2;
using LANVC.Security.Certificates;
using LANVC.Security.RateLimiting;
using LANVC.Shared.Models;
using LANVC.Shared.Protocol;
using LANVC.Transport.Abstractions;
using LANVC.Transport.Quic;
using LANVC.Transport.WebSocketTransport;

namespace LANVC.Server;

public sealed class HubServer : IAsyncDisposable
{
    private readonly ServerConfig _config;
    private readonly HubAndSpokeRouter _router;
    private readonly TokenBucketRateLimiter _rateLimiter = new(maxFailedAttempts: 5, windowMinutes: 1);
    private readonly QuicTransportServer _quicServer = new();
    private readonly WebSocketTransportServer _wsServer = new();
    private int _nextPeerId = 1;
    private X509Certificate2? _serverCert;
    private CancellationTokenSource? _cts;
    private bool _running;

    public ServerConfig Config => _config;
    public HubAndSpokeRouter Router => _router;
    public string CertificateFingerprint => _serverCert != null ? CertificateManager.GetSha256Fingerprint(_serverCert) : string.Empty;

    public event Action<string>? OnLog;

    public HubServer(ServerConfig config)
    {
        _config = config;
        var ipPool = new VirtualIpPool(config.VirtualSubnet, config.ServerVirtualIp, config.MaxClients);
        _router = new HubAndSpokeRouter(ipPool);
        _router.OnLog += msg => OnLog?.Invoke(msg);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();

        OnLog?.Invoke($"[Server] Initializing LANVC Server on {_config.ListenAddress} (Max clients: {_config.MaxClients})...");

        // Generate or load TLS 1.3 certificate
        _serverCert = CertificateManager.GenerateServerCertificate("LANVC-Server");
        string fingerprint = CertificateFingerprint;
        OnLog?.Invoke($"[Security] TLS 1.3 Certificate ready. Fingerprint: {fingerprint}");

        // Start QUIC Server (UDP)
        try
        {
            await _quicServer.StartAsync(_config.ListenAddress, _config.QuicPort, _serverCert, HandleClientAsync, _cts.Token).ConfigureAwait(false);
            OnLog?.Invoke($"[Transport] QUIC Server listening on UDP port {_config.QuicPort}");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[Transport] Warning: QUIC server failed to start on port {_config.QuicPort}: {ex.Message}");
        }

        // Start WebSocket Server (TCP)
        try
        {
            await _wsServer.StartAsync(_config.ListenAddress, _config.WebSocketPort, _serverCert, HandleClientAsync, _cts.Token).ConfigureAwait(false);
            OnLog?.Invoke($"[Transport] WebSocket Server listening on TCP/HTTPS port {_config.WebSocketPort}");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[Transport] Warning: WebSocket server failed to start on port {_config.WebSocketPort}: {ex.Message}");
        }

        _ = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        OnLog?.Invoke("[Server] LANVC Hub Server is fully operational!");
    }

    private async Task HandleClientAsync(ITransportConnection connection)
    {
        IPAddress clientIp = IPAddress.Loopback;
        if (IPEndPoint.TryParse(connection.RemoteEndPointString, out var ep))
        {
            clientIp = ep.Address;
        }

        // 1. Rate Limiting Check
        if (_rateLimiter.IsBanned(clientIp, out var banTime))
        {
            OnLog?.Invoke($"[Security] Blocked connection attempt from banned IP: {clientIp} (Remaining ban: {banTime.TotalMinutes:F1}m)");
            await SendHandshakeErrorAsync(connection, HandshakeStatusCode.RateLimited, "IP temporarily banned due to excessive failed attempts").ConfigureAwait(false);
            await connection.CloseAsync().ConfigureAwait(false);
            return;
        }

        byte[] rxBuffer = ArrayPool<byte>.Shared.Rent(65536);
        ServerPeerSession? session = null;

        try
        {
            // 2. Await HandshakeRequest
            int bytesRead = await connection.ReceiveFrameAsync(rxBuffer, _cts?.Token ?? default).ConfigureAwait(false);
            HandshakeRequest? req = null;
            {
                if (bytesRead > 0 && FrameSerializer.TryUnpack(rxBuffer.AsSpan(0, bytesRead), out var msgType, out var payload) && msgType == MessageType.HandshakeRequest)
                {
                    req = HandshakeRequest.Deserialize(payload);
                }
            }

            if (req == null)
            {
                OnLog?.Invoke($"[Server] Handshake failed or protocol violation from {connection.RemoteEndPointString}");
                await connection.CloseAsync().ConfigureAwait(false);
                return;
            }

            // 3. Password Verification (Argon2id)
            if (!string.IsNullOrEmpty(_config.PasswordHash))
            {
                bool valid = Argon2PasswordHasher.VerifyPassword(req.Password, _config.PasswordHash, _config.PasswordSaltHex);
                if (!valid)
                {
                    _rateLimiter.RecordFailure(clientIp);
                    OnLog?.Invoke($"[Security] Invalid password from {clientIp} ({req.ClientName}). Recorded failure.");
                    await SendHandshakeErrorAsync(connection, HandshakeStatusCode.InvalidPassword, "Invalid network password").ConfigureAwait(false);
                    await connection.CloseAsync().ConfigureAwait(false);
                    return;
                }
            }

            _rateLimiter.RecordSuccess(clientIp);

            // 4. Client Limit Check & Virtual IP Allocation
            ushort peerId = (ushort)Interlocked.Increment(ref _nextPeerId);
            if (!_router.IpPool.TryAllocate(peerId, req.PreferredVirtualIp, out uint assignedIp))
            {
                OnLog?.Invoke($"[Server] Connection rejected: server is full (max {_config.MaxClients} clients).");
                await SendHandshakeErrorAsync(connection, HandshakeStatusCode.ServerFull, "Server is full").ConfigureAwait(false);
                await connection.CloseAsync().ConfigureAwait(false);
                return;
            }

            // 5. Send HandshakeResponse
            var resp = new HandshakeResponse
            {
                Status = HandshakeStatusCode.Success,
                AssignedVirtualIp = assignedIp,
                SubnetMask = _router.IpPool.SubnetMask,
                ServerVirtualIp = _router.IpPool.ServerIp,
                PeerId = peerId,
                KeepaliveSeconds = 15,
                Message = "Connected to LANVC Network"
            };

            using (var ms = new MemoryStream())
            {
                resp.Serialize(ms);
                byte[] respFrame = FrameSerializer.Pack(MessageType.HandshakeResponse, ms.ToArray());
                await connection.SendFrameAsync(respFrame).ConfigureAwait(false);
            }

            // 6. Register Session
            session = new ServerPeerSession(peerId, assignedIp, string.IsNullOrWhiteSpace(req.ClientName) ? $"Peer-{peerId}" : req.ClientName, connection);
            _router.RegisterPeer(session);
            OnLog?.Invoke($"[Server] Client #{peerId} '{session.Hostname}' authenticated! Virtual IP: {IpPacketParser.ToIpString(assignedIp)} via {connection.Type}");

            // Broadcast peer list to all
            await BroadcastPeerListAsync().ConfigureAwait(false);

            // 7. Receive Loop
            while (connection.IsConnected && !(_cts?.IsCancellationRequested ?? false))
            {
                int frameLen = await connection.ReceiveFrameAsync(rxBuffer, _cts?.Token ?? default).ConfigureAwait(false);
                if (frameLen <= 0) break;

                MessageType type = MessageType.None;
                byte[]? frameData = null;

                {
                    if (FrameSerializer.TryUnpack(rxBuffer.AsSpan(0, frameLen), out type, out var framePayload))
                    {
                        frameData = framePayload.ToArray();
                    }
                }

                if (frameData != null)
                {
                    switch (type)
                    {
                        case MessageType.DataPacket:
                            // Raw IP packet routing
                            await _router.RoutePacketAsync(frameData, session).ConfigureAwait(false);
                            break;

                        case MessageType.GameDiscovery:
                            // Broadcast game discovery to all other peers
                            byte[] discFrame = FrameSerializer.Pack(MessageType.GameDiscovery, frameData);
                            await _router.BroadcastToAllAsync(discFrame, excludePeerId: peerId).ConfigureAwait(false);
                            break;

                        case MessageType.Ping:
                            // Reply Pong
                            var pingMsg = PingMessage.Deserialize(frameData);
                            byte[] pongPayload = new byte[8];
                            pingMsg.Serialize(pongPayload);
                            byte[] pongFrame = FrameSerializer.Pack(MessageType.Pong, pongPayload);
                            await connection.SendFrameAsync(pongFrame).ConfigureAwait(false);
                            break;

                        case MessageType.DisconnectNotice:
                            // Client disconnecting cleanly
                            return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[Server] Session exception for peer #{session?.PeerId}: {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rxBuffer);

            if (session != null)
            {
                _router.UnregisterPeer(session.PeerId);
                session.Close();
                OnLog?.Invoke($"[Server] Client #{session.PeerId} '{session.Hostname}' disconnected.");
                await BroadcastPeerListAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task SendHandshakeErrorAsync(ITransportConnection connection, HandshakeStatusCode code, string message)
    {
        var resp = new HandshakeResponse
        {
            Status = code,
            Message = message
        };
        using var ms = new MemoryStream();
        resp.Serialize(ms);
        byte[] frame = FrameSerializer.Pack(MessageType.HandshakeResponse, ms.ToArray());
        await connection.SendFrameAsync(frame).ConfigureAwait(false);
    }

    public async Task BroadcastPeerListAsync()
    {
        var peers = _router.GetAllPeers();
        var update = new PeerListUpdate();
        var now = DateTime.UtcNow;

        foreach (var p in peers)
        {
            update.Peers.Add(new PeerDto
            {
                PeerId = p.PeerId,
                VirtualIp = p.VirtualIp,
                Hostname = p.Hostname,
                LatencyMs = p.LatencyMs,
                OnlineDurationSeconds = (uint)Math.Max(0, (now - p.ConnectedAt).TotalSeconds),
                IsOnline = p.IsActive
            });
        }

        using var ms = new MemoryStream();
        update.Serialize(ms);
        byte[] frame = FrameSerializer.Pack(MessageType.PeerListUpdate, ms.ToArray());
        await _router.BroadcastToAllAsync(frame).ConfigureAwait(false);
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _running)
        {
            try
            {
                await Task.Delay(5000, ct).ConfigureAwait(false);
                await BroadcastPeerListAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    public async Task StopAsync()
    {
        if (!_running) return;
        _running = false;
        _cts?.Cancel();

        await _quicServer.StopAsync().ConfigureAwait(false);
        await _wsServer.StopAsync().ConfigureAwait(false);

        foreach (var peer in _router.GetAllPeers())
        {
            peer.Close();
        }

        OnLog?.Invoke("[Server] LANVC Hub Server stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
        _serverCert?.Dispose();
    }
}
