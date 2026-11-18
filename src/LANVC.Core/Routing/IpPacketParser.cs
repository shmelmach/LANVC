using System.Buffers.Binary;
using System.Net;

namespace LANVC.Core.Routing;

public static class IpPacketParser
{
    public const byte ProtocolIcmp = 1;
    public const byte ProtocolTcp = 6;
    public const byte ProtocolUdp = 17;

    public const uint LimitedBroadcast = 0xFFFFFFFF; // 255.255.255.255

    public static bool IsValidIPv4(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 20)
            return false;

        byte versionAndIhl = packet[0];
        byte version = (byte)(versionAndIhl >> 4);
        if (version != 4)
            return false;

        byte ihl = (byte)(versionAndIhl & 0x0F);
        int headerLength = ihl * 4;
        if (headerLength < 20 || headerLength > packet.Length)
            return false;

        ushort totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2));
        if (totalLength > packet.Length)
            return false;

        return true;
    }

    public static byte GetProtocol(ReadOnlySpan<byte> packet) => packet[9];

    public static uint GetSourceIp(ReadOnlySpan<byte> packet)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(12, 4));
    }

    public static uint GetDestinationIp(ReadOnlySpan<byte> packet)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(16, 4));
    }

    public static bool IsBroadcast(uint ip, uint subnetBroadcast)
    {
        return ip == LimitedBroadcast || ip == subnetBroadcast;
    }

    public static bool IsMulticast(uint ip)
    {
        // 224.0.0.0 to 239.255.255.255 (top 4 bits == 1110 = 0xE)
        return (ip >> 28) == 0x0E;
    }

    public static string ToIpString(uint ip)
    {
        byte b1 = (byte)((ip >> 24) & 0xFF);
        byte b2 = (byte)((ip >> 16) & 0xFF);
        byte b3 = (byte)((ip >> 8) & 0xFF);
        byte b4 = (byte)(ip & 0xFF);
        return $"{b1}.{b2}.{b3}.{b4}";
    }

    public static uint FromIpString(string ipString)
    {
        if (IPAddress.TryParse(ipString, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
                return BinaryPrimitives.ReadUInt32BigEndian(bytes);
        }
        return 0;
    }

    public static ushort CalculateChecksum(ReadOnlySpan<byte> header)
    {
        uint sum = 0;
        int i = 0;
        while (i < header.Length - 1)
        {
            sum += BinaryPrimitives.ReadUInt16BigEndian(header.Slice(i, 2));
            i += 2;
        }

        if (i < header.Length)
        {
            sum += (uint)(header[i] << 8);
        }

        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }
}
