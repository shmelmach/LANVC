using System.Net;
using System.Net.Sockets;
using System.Text;
using LANVC.Shared.Protocol;

namespace LANVC.Core.Discovery;

public sealed class GameDiscoveryRelay : IDisposable
{
    public const int MinecraftPort = 4445;
    public const string MinecraftMulticastGroup = "224.0.2.60";

    private Socket? _listenerSocket;
    private Socket? _senderSocket;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public event Action<GameDiscoveryMessage>? OnLocalDiscoveryCaptured;
    public event Action<string>? OnLog;

    public void Start()
    {
        if (_listenerSocket != null) return;

        try
        {
            _cts = new CancellationTokenSource();

            _senderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true
            };

            _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _listenerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listenerSocket.Bind(new IPEndPoint(IPAddress.Any, MinecraftPort));

            try
            {
                _listenerSocket.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.AddMembership,
                    new MulticastOption(IPAddress.Parse(MinecraftMulticastGroup), IPAddress.Any));
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[Discovery] Warning: Multicast membership join notice: {ex.Message}");
            }

            Task.Run(() => ListenLoopAsync(_cts.Token));
            OnLog?.Invoke($"[Discovery] Game Discovery Relay started for Minecraft LAN (port {MinecraftPort})");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[Discovery] Notice: Could not bind discovery port {MinecraftPort}: {ex.Message}");
        }
    }

    public void ForwardRemoteDiscovery(GameDiscoveryMessage message, IPAddress targetVirtualIp)
    {
        if (_disposed || _senderSocket == null || message.Payload.Length == 0)
            return;

        try
        {
            // Send to localhost port so Minecraft client on this machine receives it
            var localEp = new IPEndPoint(IPAddress.Loopback, message.TargetPort);
            _senderSocket.SendTo(message.Payload, localEp);

            // Also send to Minecraft multicast group
            var mcastEp = new IPEndPoint(IPAddress.Parse(MinecraftMulticastGroup), message.TargetPort);
            _senderSocket.SendTo(message.Payload, mcastEp);
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[Discovery] Forward error: {ex.Message}");
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[4096];
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested && !_disposed && _listenerSocket != null)
        {
            try
            {
                var result = await _listenerSocket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEp);
                if (result.ReceivedBytes > 0)
                {
                    byte[] payload = new byte[result.ReceivedBytes];
                    Array.Copy(buffer, payload, result.ReceivedBytes);

                    // Check if it's Minecraft discovery message [MOTD]...[/MOTD]
                    string text = Encoding.UTF8.GetString(payload);
                    if (text.Contains("[MOTD]") && text.Contains("[/MOTD]"))
                    {
                        var msg = new GameDiscoveryMessage
                        {
                            TargetPort = MinecraftPort,
                            Payload = payload
                        };
                        OnLocalDiscoveryCaptured?.Invoke(msg);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[Discovery] Listener error: {ex.Message}");
                await Task.Delay(1000, ct);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _listenerSocket?.Dispose();
        _senderSocket?.Dispose();
    }
}
