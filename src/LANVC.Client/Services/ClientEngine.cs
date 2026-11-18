using System.Buffers;
using System.IO;
using LANVC.Adapter;
using LANVC.Core.Discovery;
using LANVC.Core.Routing;
using LANVC.Shared.Models;
using LANVC.Shared.Protocol;
using LANVC.Transport.Abstractions;
using LANVC.Transport.Failover;

namespace LANVC.Client.Services;

public sealed class ClientEngine : IAsyncDisposable
{
    private readonly TransportFailoverManager _failoverManager = new();
    private readonly WintunAdapter _wintunAdapter = new("LANVC", "LANVC_Client");
    private readonly GameDiscoveryRelay _discoveryRelay = new();
    private readonly NetworkStatistics _stats = new();

    private ITransportConnection? _connection;
    private CancellationTokenSource? _cts;
    private bool _connecting;
    private bool _connected;

    public NetworkStatistics Statistics => _stats;
    public bool IsConnected => _connected && _connection != null && _connection.IsConnected;
    public uint AssignedVirtualIp { get; private set; }
    public string AssignedVirtualIpString => AssignedVirtualIp != 0 ? IpPacketParser.ToIpString(AssignedVirtualIp) : "0.0.0.0";
    public TransportType ActiveTransport => _connection?.Type ?? TransportType.Quic;
    public ushort PeerId { get; private set; }
    public DateTime? ConnectedAt { get; private set; }

    public event Action<string>? OnLog;
    public event Action<List<PeerDto>>? OnPeerListUpdated;
    public event Action<bool>? OnConnectionStateChanged;
    public event Action<TransportType>? OnTransportChanged;

    public ClientEngine()
    {
        _failoverManager.OnLog += msg => OnLog?.Invoke(msg);
        _failoverManager.OnTransportChanged += t => OnTransportChanged?.Invoke(t);
        _wintunAdapter.OnLog += msg => OnLog?.Invoke(msg);
        _discoveryRelay.OnLog += msg => OnLog?.Invoke(msg);

        _discoveryRelay.OnLocalDiscoveryCaptured += OnLocalDiscovery;
        _wintunAdapter.OnPacketReceived += OnWintunPacket;
    }

    public async Task ConnectAsync(
        string host,
        int port,
        string password,
        string username,
        TransportPreference preference = TransportPreference.Auto,
        string? expectedCertFingerprint = null,
        ushort mtu = 1420)
    {
        if (_connecting || _connected) return;
        _connecting = true;
        _cts = new CancellationTokenSource();

        try
        {
            OnLog?.Invoke($"[Engine] Connecting to server {host}:{port} (User: {username})...");

            _connection = await _failoverManager.ConnectWithFallbackAsync(
                host,
                port,
                preference,
                expectedCertFingerprint,
                _cts.Token).ConfigureAwait(false);

            _connection.OnDisconnected += (conn, ex) =>
            {
                OnLog?.Invoke($"[Engine] Connection dropped: {ex?.Message ?? "Server closed connection"}");
                _ = DisconnectAsync();
            };

            // Handshake
            var req = new HandshakeRequest
            {
                ProtocolVersion = 1,
                ClientName = username,
                Password = password
            };

            using (var ms = new MemoryStream())
            {
                req.Serialize(ms);
                byte[] frame = FrameSerializer.Pack(MessageType.HandshakeRequest, ms.ToArray());
                await _connection.SendFrameAsync(frame, _cts.Token).ConfigureAwait(false);
            }

            // Receive HandshakeResponse
            byte[] rxBuf = new byte[4096];
            int read = await _connection.ReceiveFrameAsync(rxBuf, _cts.Token).ConfigureAwait(false);
            HandshakeResponse? resp = null;
            {
                if (read > 0 && FrameSerializer.TryUnpack(rxBuf.AsSpan(0, read), out var type, out var payload) && type == MessageType.HandshakeResponse)
                {
                    resp = HandshakeResponse.Deserialize(payload);
                }
            }

            if (resp == null)
            {
                throw new InvalidOperationException("Failed to receive valid HandshakeResponse from server");
            }
            if (resp.Status != HandshakeStatusCode.Success)
            {
                string errMsg = resp.Status switch
                {
                    HandshakeStatusCode.InvalidPassword => "Неверный пароль сети!",
                    HandshakeStatusCode.ServerFull => "Сервер переполнен (максимум 10 участников).",
                    HandshakeStatusCode.RateLimited => "Превышен лимит попыток. Временная блокировка.",
                    _ => $"Ошибка авторизации: {resp.Message}"
                };
                throw new InvalidOperationException(errMsg);
            }

            AssignedVirtualIp = resp.AssignedVirtualIp;
            PeerId = resp.PeerId;
            ConnectedAt = DateTime.UtcNow;

            string ipStr = IpPacketParser.ToIpString(AssignedVirtualIp);
            string maskStr = IpPacketParser.ToIpString(resp.SubnetMask);
            OnLog?.Invoke($"[Engine] Authorized! Assigned IP: {ipStr}, Mask: {maskStr}, Peer #{PeerId}");

            // Start Wintun Adapter
            bool adapterStarted = _wintunAdapter.Start(ipStr, maskStr, mtu);
            if (!adapterStarted)
            {
                OnLog?.Invoke("[Engine] Warning: Could not initialize Wintun network adapter. Please run as Administrator.");
            }

            // Start Game Discovery Relay (Minecraft & LAN games)
            _discoveryRelay.Start();

            _connected = true;
            _connecting = false;
            OnConnectionStateChanged?.Invoke(true);

            // Start receive loop & ping loop
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            _ = Task.Run(() => HeartbeatAndStatsLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            _connecting = false;
            OnLog?.Invoke($"[Engine] Connection failed: {ex.Message}");
            await DisconnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void OnWintunPacket(byte[] packet)
    {
        if (!_connected || _connection == null || !_connection.IsConnected)
            return;

        try
        {
            byte[] frame = FrameSerializer.PackDataPacket(packet);
            _stats.RecordSent(packet.Length);
            _ = _connection.SendFrameAsync(frame);
        }
        catch { }
    }

    private void OnLocalDiscovery(GameDiscoveryMessage msg)
    {
        if (!_connected || _connection == null || !_connection.IsConnected)
            return;

        try
        {
            using var ms = new MemoryStream();
            msg.Serialize(ms);
            byte[] frame = FrameSerializer.Pack(MessageType.GameDiscovery, ms.ToArray());
            _ = _connection.SendFrameAsync(frame);
        }
        catch { }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        byte[] rxBuffer = ArrayPool<byte>.Shared.Rent(65536);

        try
        {
            while (!ct.IsCancellationRequested && _connected && _connection != null && _connection.IsConnected)
            {
                int frameLen = await _connection.ReceiveFrameAsync(rxBuffer, ct).ConfigureAwait(false);
                if (frameLen <= 0) break;

                MessageType type = MessageType.None;
                byte[]? payloadCopy = null;

                {
                    if (FrameSerializer.TryUnpack(rxBuffer.AsSpan(0, frameLen), out type, out var framePayload))
                    {
                        payloadCopy = framePayload.ToArray();
                    }
                }

                if (payloadCopy != null)
                {
                    switch (type)
                    {
                        case MessageType.DataPacket:
                            _stats.RecordReceived(payloadCopy.Length);
                            _wintunAdapter.SendPacket(payloadCopy);
                            break;

                        case MessageType.PeerListUpdate:
                            var update = PeerListUpdate.Deserialize(payloadCopy);
                            OnPeerListUpdated?.Invoke(update.Peers);
                            break;

                        case MessageType.GameDiscovery:
                            var disc = GameDiscoveryMessage.Deserialize(payloadCopy);
                            _discoveryRelay.ForwardRemoteDiscovery(disc, System.Net.IPAddress.Parse(AssignedVirtualIpString));
                            break;

                        case MessageType.Pong:
                            var pong = PingMessage.Deserialize(payloadCopy);
                            long rttTicks = DateTime.UtcNow.Ticks - pong.TimestampTicks;
                            int rttMs = (int)(rttTicks / TimeSpan.TicksPerMillisecond);
                            _stats.LatencyMs = Math.Max(1, rttMs);
                            break;

                        case MessageType.DisconnectNotice:
                            var notice = DisconnectNotice.Deserialize(payloadCopy);
                            OnLog?.Invoke($"[Engine] Server sent disconnect notice: {notice.Reason}");
                            return;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[Engine] Receive loop terminated: {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rxBuffer);
            _ = DisconnectAsync();
        }
    }

    private async Task HeartbeatAndStatsLoopAsync(CancellationToken ct)
    {
        int pingCounter = 0;

        while (!ct.IsCancellationRequested && _connected && _connection != null && _connection.IsConnected)
        {
            try
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);

                // Update download/upload speeds every second
                _stats.Tick();

                // Send ping every 3 seconds
                pingCounter++;
                if (pingCounter >= 3)
                {
                    pingCounter = 0;
                    var ping = new PingMessage { TimestampTicks = DateTime.UtcNow.Ticks };
                    byte[] pingBytes = new byte[8];
                    ping.Serialize(pingBytes);
                    byte[] frame = FrameSerializer.Pack(MessageType.Ping, pingBytes);
                    await _connection.SendFrameAsync(frame, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    public async Task DisconnectAsync()
    {
        if (!_connected && !_connecting) return;
        _connected = false;
        _connecting = false;

        _cts?.Cancel();

        // Send disconnect notice if still connected
        if (_connection != null && _connection.IsConnected)
        {
            try
            {
                var notice = new DisconnectNotice { ReasonCode = 0, Reason = "User disconnected" };
                using var ms = new MemoryStream();
                notice.Serialize(ms);
                byte[] frame = FrameSerializer.Pack(MessageType.DisconnectNotice, ms.ToArray());
                await _connection.SendFrameAsync(frame).ConfigureAwait(false);
            }
            catch { }
        }

        // Stop virtual adapter
        _wintunAdapter.Stop();

        // Stop game discovery relay
        _discoveryRelay.Dispose();

        // Close transport
        if (_connection != null)
        {
            try { await _connection.CloseAsync().ConfigureAwait(false); } catch { }
            _connection = null;
        }

        ConnectedAt = null;
        AssignedVirtualIp = 0;
        OnLog?.Invoke("[Engine] Disconnected from LANVC network.");
        OnConnectionStateChanged?.Invoke(false);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _wintunAdapter.Dispose();
        _discoveryRelay.Dispose();
        _cts?.Dispose();
    }
}
