using LANVC.Shared.Models;
using LANVC.Transport.Abstractions;
using LANVC.Transport.Quic;
using LANVC.Transport.WebSocketTransport;

namespace LANVC.Transport.Failover;

public sealed class TransportFailoverManager
{
    private readonly QuicTransportClient _quicClient = new();
    private readonly WebSocketTransportClient _wsClient = new();

    public event Action<string>? OnLog;
    public event Action<TransportType>? OnTransportChanged;

    public async Task<ITransportConnection> ConnectWithFallbackAsync(
        string host,
        int port,
        TransportPreference preference,
        string? expectedCertFingerprint,
        CancellationToken ct = default)
    {
        switch (preference)
        {
            case TransportPreference.QuicOnly:
                OnLog?.Invoke($"[Transport] Connecting via QUIC (UDP) to {host}:{port}...");
                var quicOnlyConn = await _quicClient.ConnectAsync(host, port, expectedCertFingerprint, ct).ConfigureAwait(false);
                OnTransportChanged?.Invoke(TransportType.Quic);
                return quicOnlyConn;

            case TransportPreference.WebSocketOnly:
                OnLog?.Invoke($"[Transport] Connecting via WebSocket (WSS 443) to {host}:{port}...");
                var wsOnlyConn = await _wsClient.ConnectAsync(host, port, expectedCertFingerprint, ct).ConfigureAwait(false);
                OnTransportChanged?.Invoke(TransportType.WebSocket);
                return wsOnlyConn;

            case TransportPreference.Auto:
            default:
                // Auto: try QUIC with timeout, then fallback to WebSocket
                OnLog?.Invoke($"[Transport] Auto-mode: Attempting primary transport QUIC (UDP) on {host}:{port}...");
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(3.5));

                    var quicConn = await _quicClient.ConnectAsync(host, port, expectedCertFingerprint, timeoutCts.Token).ConfigureAwait(false);
                    OnLog?.Invoke("[Transport] Primary transport QUIC connected successfully!");
                    OnTransportChanged?.Invoke(TransportType.Quic);
                    return quicConn;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) throw;

                    OnLog?.Invoke($"[Transport] QUIC connection failed or UDP blocked ({ex.Message}). Failing over to WebSocket (HTTPS)...");
                }

                // Fallback to WebSocket
                OnLog?.Invoke($"[Transport] Connecting fallback transport WebSocket (WSS) to {host}:{port}...");
                var fallbackConn = await _wsClient.ConnectAsync(host, port, expectedCertFingerprint, ct).ConfigureAwait(false);
                OnLog?.Invoke("[Transport] Fallback transport WebSocket connected successfully!");
                OnTransportChanged?.Invoke(TransportType.WebSocket);
                return fallbackConn;
        }
    }
}
