using System.Buffers.Binary;
using System.Text;

namespace LANVC.Shared.Protocol;

public sealed class PeerDto
{
    public ushort PeerId { get; set; }
    public uint VirtualIp { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public ushort LatencyMs { get; set; }
    public uint OnlineDurationSeconds { get; set; }
    public bool IsOnline { get; set; } = true;

    public string VirtualIpString
    {
        get
        {
            byte b1 = (byte)((VirtualIp >> 24) & 0xFF);
            byte b2 = (byte)((VirtualIp >> 16) & 0xFF);
            byte b3 = (byte)((VirtualIp >> 8) & 0xFF);
            byte b4 = (byte)(VirtualIp & 0xFF);
            return $"{b1}.{b2}.{b3}.{b4}";
        }
    }

    public string FormattedOnlineDuration
    {
        get
        {
            var ts = TimeSpan.FromSeconds(OnlineDurationSeconds);
            return ts.Hours > 0
                ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }
}

public sealed class PeerListUpdate
{
    public List<PeerDto> Peers { get; set; } = new();

    public void Serialize(Stream destination)
    {
        destination.WriteByte((byte)Math.Min(Peers.Count, 255));
        Span<byte> buf = stackalloc byte[13];

        foreach (var peer in Peers.Take(255))
        {
            BinaryPrimitives.WriteUInt16BigEndian(buf[0..2], peer.PeerId);
            BinaryPrimitives.WriteUInt32BigEndian(buf[2..6], peer.VirtualIp);
            BinaryPrimitives.WriteUInt16BigEndian(buf[6..8], peer.LatencyMs);
            BinaryPrimitives.WriteUInt32BigEndian(buf[8..12], peer.OnlineDurationSeconds);
            buf[12] = (byte)(peer.IsOnline ? 1 : 0);
            destination.Write(buf);

            byte[] hostBytes = Encoding.UTF8.GetBytes(peer.Hostname);
            destination.WriteByte((byte)Math.Min(hostBytes.Length, 255));
            destination.Write(hostBytes, 0, Math.Min(hostBytes.Length, 255));
        }
    }

    public static PeerListUpdate Deserialize(ReadOnlySpan<byte> source)
    {
        var result = new PeerListUpdate();
        if (source.IsEmpty)
            return result;

        byte count = source[0];
        int offset = 1;

        for (int i = 0; i < count; i++)
        {
            if (offset + 13 > source.Length)
                break;

            var peer = new PeerDto
            {
                PeerId = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset, 2)),
                VirtualIp = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset + 2, 4)),
                LatencyMs = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset + 6, 2)),
                OnlineDurationSeconds = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset + 8, 4)),
                IsOnline = source[offset + 12] != 0
            };
            offset += 13;

            if (offset < source.Length)
            {
                byte hLen = source[offset++];
                if (offset + hLen <= source.Length)
                {
                    peer.Hostname = Encoding.UTF8.GetString(source.Slice(offset, hLen));
                    offset += hLen;
                }
            }

            result.Peers.Add(peer);
        }

        return result;
    }
}

public sealed class PingMessage
{
    public long TimestampTicks { get; set; }

    public void Serialize(Span<byte> destination)
    {
        BinaryPrimitives.WriteInt64BigEndian(destination, TimestampTicks);
    }

    public static PingMessage Deserialize(ReadOnlySpan<byte> source)
    {
        return new PingMessage
        {
            TimestampTicks = source.Length >= 8 ? BinaryPrimitives.ReadInt64BigEndian(source[..8]) : DateTime.UtcNow.Ticks
        };
    }
}

public sealed class DisconnectNotice
{
    public byte ReasonCode { get; set; }
    public string Reason { get; set; } = string.Empty;

    public void Serialize(Stream destination)
    {
        destination.WriteByte(ReasonCode);
        byte[] reasonBytes = Encoding.UTF8.GetBytes(Reason);
        destination.WriteByte((byte)Math.Min(reasonBytes.Length, 255));
        destination.Write(reasonBytes, 0, Math.Min(reasonBytes.Length, 255));
    }

    public static DisconnectNotice Deserialize(ReadOnlySpan<byte> source)
    {
        var notice = new DisconnectNotice();
        if (source.IsEmpty)
            return notice;

        notice.ReasonCode = source[0];
        if (source.Length > 2)
        {
            byte len = source[1];
            if (2 + len <= source.Length)
            {
                notice.Reason = Encoding.UTF8.GetString(source.Slice(2, len));
            }
        }
        return notice;
    }
}

public sealed class GameDiscoveryMessage
{
    public ushort TargetPort { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public void Serialize(Stream destination)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(buf[0..2], TargetPort);
        BinaryPrimitives.WriteUInt16BigEndian(buf[2..4], (ushort)Math.Min(Payload.Length, 65535));
        destination.Write(buf);
        destination.Write(Payload);
    }

    public static GameDiscoveryMessage Deserialize(ReadOnlySpan<byte> source)
    {
        var msg = new GameDiscoveryMessage();
        if (source.Length < 4)
            return msg;

        msg.TargetPort = BinaryPrimitives.ReadUInt16BigEndian(source[0..2]);
        ushort len = BinaryPrimitives.ReadUInt16BigEndian(source[2..4]);

        if (source.Length >= 4 + len)
        {
            msg.Payload = source.Slice(4, len).ToArray();
        }

        return msg;
    }
}
