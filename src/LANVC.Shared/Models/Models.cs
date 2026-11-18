namespace LANVC.Shared.Models;

public sealed class NetworkStatistics
{
    private long _bytesReceived;
    private long _bytesSent;
    private long _packetsReceived;
    private long _packetsSent;
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private DateTime _lastCalculationTime = DateTime.UtcNow;

    public long TotalBytesReceived => Interlocked.Read(ref _bytesReceived);
    public long TotalBytesSent => Interlocked.Read(ref _bytesSent);
    public long TotalPacketsReceived => Interlocked.Read(ref _packetsReceived);
    public long TotalPacketsSent => Interlocked.Read(ref _packetsSent);

    public double DownloadSpeedBytesPerSec { get; private set; }
    public double UploadSpeedBytesPerSec { get; private set; }
    public int LatencyMs { get; set; }

    public void RecordReceived(int byteCount)
    {
        Interlocked.Add(ref _bytesReceived, byteCount);
        Interlocked.Increment(ref _packetsReceived);
    }

    public void RecordSent(int byteCount)
    {
        Interlocked.Add(ref _bytesSent, byteCount);
        Interlocked.Increment(ref _packetsSent);
    }

    public void Tick()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastCalculationTime).TotalSeconds;
        if (elapsed <= 0.1) return;

        long currentRecv = TotalBytesReceived;
        long currentSent = TotalBytesSent;

        DownloadSpeedBytesPerSec = (currentRecv - _lastBytesReceived) / elapsed;
        UploadSpeedBytesPerSec = (currentSent - _lastBytesSent) / elapsed;

        _lastBytesReceived = currentRecv;
        _lastBytesSent = currentSent;
        _lastCalculationTime = now;
    }

    public string FormatDownloadSpeed() => FormatSpeed(DownloadSpeedBytesPerSec);
    public string FormatUploadSpeed() => FormatSpeed(UploadSpeedBytesPerSec);

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024)
            return $"{bytesPerSec / (1024 * 1024):0.0} МБ/с";
        if (bytesPerSec >= 1024)
            return $"{bytesPerSec / 1024:0.0} КБ/с";
        return $"{bytesPerSec:0} Б/с";
    }
}

public enum TransportPreference
{
    Auto = 0,
    QuicOnly = 1,
    WebSocketOnly = 2
}

public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Моя сеть";
    public string ServerAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 443;
    public TransportPreference Transport { get; set; } = TransportPreference.Auto;
    public string Username { get; set; } = Environment.MachineName;
    public bool AutoReconnect { get; set; } = true;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
}

public sealed class ServerConfig
{
    public string ListenAddress { get; set; } = "0.0.0.0";
    public int QuicPort { get; set; } = 443;
    public int WebSocketPort { get; set; } = 443;
    public string VirtualSubnet { get; set; } = "10.77.0.0/24";
    public string ServerVirtualIp { get; set; } = "10.77.0.1";
    public int MaxClients { get; set; } = 10;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSaltHex { get; set; } = string.Empty;
    public string CertThumbprint { get; set; } = string.Empty;
    public bool EnableBroadcastRelay { get; set; } = true;
}

public sealed class ClientConfig
{
    public List<ConnectionProfile> Profiles { get; set; } = new();
    public Guid? ActiveProfileId { get; set; }
    public TransportPreference DefaultTransport { get; set; } = TransportPreference.Auto;
    public bool AutoStartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public ushort VirtualMtu { get; set; } = 1420;
    public bool EnableDiscoveryRelay { get; set; } = true;
}
