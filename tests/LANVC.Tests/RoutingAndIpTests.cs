using System.Buffers.Binary;
using System.Net;
using LANVC.Core.Routing;
using LANVC.Core.Sessions;
using LANVC.Shared.Protocol;
using Xunit;

namespace LANVC.Tests;

public class RoutingAndIpTests
{
    private sealed class MockPeerSession : IPeerSession
    {
        public ushort PeerId { get; set; }
        public uint VirtualIp { get; set; }
        public string Hostname { get; set; } = "MockPeer";
        public IPEndPoint? RemoteEndPoint { get; set; }
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
        public ushort LatencyMs { get; set; } = 10;
        public bool IsActive => !_closed;

        private bool _closed;
        public readonly List<byte[]> ReceivedPackets = new();

        public ValueTask SendDataPacketAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (!_closed)
            {
                ReceivedPackets.Add(frame.ToArray());
            }
            return ValueTask.CompletedTask;
        }

        public void Close() => _closed = true;
    }

    private static byte[] CreateMockIpv4Packet(uint srcIp, uint dstIp, byte protocol = 17, byte[]? payload = null)
    {
        payload ??= new byte[] { 1, 2, 3, 4 };
        byte[] packet = new byte[20 + payload.Length];
        packet[0] = 0x45; // Version 4, IHL 5 (20 bytes)
        packet[1] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[8] = 64; // TTL
        packet[9] = protocol; // Protocol: 17 = UDP, 6 = TCP, 1 = ICMP
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), srcIp);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(16, 4), dstIp);

        // Header checksum
        ushort csum = IpPacketParser.CalculateChecksum(packet.AsSpan(0, 20));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10, 2), csum);

        payload.CopyTo(packet, 20);
        return packet;
    }

    [Fact]
    public void IpPacketParser_ParsesValidIpv4PacketCorrectly()
    {
        uint src = 0x0A4D0002; // 10.77.0.2
        uint dst = 0x0A4D0003; // 10.77.0.3
        byte[] packet = CreateMockIpv4Packet(src, dst, protocol: 17);

        Assert.True(IpPacketParser.IsValidIPv4(packet));
        Assert.Equal(src, IpPacketParser.GetSourceIp(packet));
        Assert.Equal(dst, IpPacketParser.GetDestinationIp(packet));
        Assert.Equal(17, IpPacketParser.GetProtocol(packet));
        Assert.Equal("10.77.0.2", IpPacketParser.ToIpString(src));
        Assert.Equal("10.77.0.3", IpPacketParser.ToIpString(dst));
    }

    [Fact]
    public void IpPacketParser_DetectsBroadcastAndMulticast()
    {
        uint subnetBroadcast = 0x0A4D00FF; // 10.77.0.255
        uint limitedBroadcast = 0xFFFFFFFF; // 255.255.255.255
        uint multicastMinecraft = 0xE000023C; // 224.0.2.60
        uint normalUnicast = 0x0A4D0004; // 10.77.0.4

        Assert.True(IpPacketParser.IsBroadcast(subnetBroadcast, subnetBroadcast));
        Assert.True(IpPacketParser.IsBroadcast(limitedBroadcast, subnetBroadcast));
        Assert.False(IpPacketParser.IsBroadcast(normalUnicast, subnetBroadcast));

        Assert.True(IpPacketParser.IsMulticast(multicastMinecraft));
        Assert.False(IpPacketParser.IsMulticast(normalUnicast));
    }

    [Fact]
    public void VirtualIpPool_AllocatesAndEnforcesMaxUsersLimit()
    {
        var pool = new VirtualIpPool(subnetCidr: "10.77.0.0/24", serverVirtualIp: "10.77.0.1", maxUsers: 3);

        // Allocate 3 peers
        Assert.True(pool.TryAllocate(1, 0, out uint ip1));
        Assert.True(pool.TryAllocate(2, 0, out uint ip2));
        Assert.True(pool.TryAllocate(3, 0, out uint ip3));

        Assert.Equal("10.77.0.2", IpPacketParser.ToIpString(ip1));
        Assert.Equal("10.77.0.3", IpPacketParser.ToIpString(ip2));
        Assert.Equal("10.77.0.4", IpPacketParser.ToIpString(ip3));

        // 4th peer must be rejected (pool is full)
        Assert.False(pool.TryAllocate(4, 0, out uint ip4));
        Assert.Equal(0u, ip4);

        // Release peer 2
        pool.Release(2);

        // Now a new peer can allocate
        Assert.True(pool.TryAllocate(5, 0, out uint ip5));
        Assert.Equal("10.77.0.3", IpPacketParser.ToIpString(ip5));
    }

    [Fact]
    public async Task HubAndSpokeRouter_RoutesUnicastBetweenTwoPeers()
    {
        var pool = new VirtualIpPool("10.77.0.0/24", "10.77.0.1", 10);
        var router = new HubAndSpokeRouter(pool);

        var peer1 = new MockPeerSession { PeerId = 1, VirtualIp = 0x0A4D0002, Hostname = "Client1" };
        var peer2 = new MockPeerSession { PeerId = 2, VirtualIp = 0x0A4D0003, Hostname = "Client2" };
        var peer3 = new MockPeerSession { PeerId = 3, VirtualIp = 0x0A4D0004, Hostname = "Client3" };

        router.RegisterPeer(peer1);
        router.RegisterPeer(peer2);
        router.RegisterPeer(peer3);

        // Peer 1 sends packet destined to Peer 2 (10.77.0.3)
        byte[] packet = CreateMockIpv4Packet(srcIp: peer1.VirtualIp, dstIp: peer2.VirtualIp);
        await router.RoutePacketAsync(packet, peer1);

        // Peer 2 must receive it
        Assert.Single(peer2.ReceivedPackets);
        // Peer 3 must NOT receive unicast
        Assert.Empty(peer3.ReceivedPackets);
        // Peer 1 (sender) must NOT receive it
        Assert.Empty(peer1.ReceivedPackets);
    }

    [Fact]
    public async Task HubAndSpokeRouter_BroadcastsToAllOtherPeers()
    {
        var pool = new VirtualIpPool("10.77.0.0/24", "10.77.0.1", 10);
        var router = new HubAndSpokeRouter(pool);

        var peer1 = new MockPeerSession { PeerId = 1, VirtualIp = 0x0A4D0002, Hostname = "Host" };
        var peer2 = new MockPeerSession { PeerId = 2, VirtualIp = 0x0A4D0003, Hostname = "Player1" };
        var peer3 = new MockPeerSession { PeerId = 3, VirtualIp = 0x0A4D0004, Hostname = "Player2" };

        router.RegisterPeer(peer1);
        router.RegisterPeer(peer2);
        router.RegisterPeer(peer3);

        // Host sends Limited Broadcast (255.255.255.255)
        byte[] bcastPacket = CreateMockIpv4Packet(srcIp: peer1.VirtualIp, dstIp: 0xFFFFFFFF);
        await router.RoutePacketAsync(bcastPacket, peer1);

        // Both Player1 and Player2 must receive it
        Assert.Single(peer2.ReceivedPackets);
        Assert.Single(peer3.ReceivedPackets);
        // Sender must NOT receive its own broadcast
        Assert.Empty(peer1.ReceivedPackets);
    }

    [Fact]
    public async Task HubAndSpokeRouter_MulticastToAllOtherPeers()
    {
        var pool = new VirtualIpPool("10.77.0.0/24", "10.77.0.1", 10);
        var router = new HubAndSpokeRouter(pool);

        var peer1 = new MockPeerSession { PeerId = 1, VirtualIp = 0x0A4D0002, Hostname = "MinecraftHost" };
        var peer2 = new MockPeerSession { PeerId = 2, VirtualIp = 0x0A4D0003, Hostname = "Player1" };
        var peer3 = new MockPeerSession { PeerId = 3, VirtualIp = 0x0A4D0004, Hostname = "Player2" };

        router.RegisterPeer(peer1);
        router.RegisterPeer(peer2);
        router.RegisterPeer(peer3);

        // Minecraft LAN Multicast: 224.0.2.60
        uint mcMulticast = 0xE000023C;
        byte[] mcPacket = CreateMockIpv4Packet(srcIp: peer1.VirtualIp, dstIp: mcMulticast);
        await router.RoutePacketAsync(mcPacket, peer1);

        Assert.Single(peer2.ReceivedPackets);
        Assert.Single(peer3.ReceivedPackets);
        Assert.Empty(peer1.ReceivedPackets);
    }

    [Fact]
    public async Task HubAndSpokeRouter_AntiSpoofing_DropsPacketWithForgedSourceIp()
    {
        var pool = new VirtualIpPool("10.77.0.0/24", "10.77.0.1", 10);
        var router = new HubAndSpokeRouter(pool);

        var peer1 = new MockPeerSession { PeerId = 1, VirtualIp = 0x0A4D0002, Hostname = "Attacker" };
        var peer2 = new MockPeerSession { PeerId = 2, VirtualIp = 0x0A4D0003, Hostname = "Target" };

        router.RegisterPeer(peer1);
        router.RegisterPeer(peer2);

        // Peer 1 attempts to forge Source IP = 10.77.0.44 instead of assigned 10.77.0.2
        uint forgedIp = 0x0A4D002C;
        byte[] packet = CreateMockIpv4Packet(srcIp: forgedIp, dstIp: peer2.VirtualIp);
        await router.RoutePacketAsync(packet, peer1);

        // Target must NOT receive spoofed packet
        Assert.Empty(peer2.ReceivedPackets);
    }
}
