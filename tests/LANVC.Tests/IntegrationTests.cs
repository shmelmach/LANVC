using System.Net;
using LANVC.Core.Routing;
using LANVC.Core.Sessions;
using LANVC.Shared.Protocol;
using Xunit;

namespace LANVC.Tests;

public class IntegrationTests
{
    private sealed class SimulatedPeer : IPeerSession
    {
        public ushort PeerId { get; set; }
        public uint VirtualIp { get; set; }
        public string Hostname { get; set; } = "Player";
        public IPEndPoint? RemoteEndPoint { get; set; }
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
        public ushort LatencyMs { get; set; } = 15;
        public bool IsActive => !_closed;

        private bool _closed;
        public readonly List<byte[]> Inbox = new();

        public ValueTask SendDataPacketAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (!_closed)
            {
                Inbox.Add(frame.ToArray());
            }
            return ValueTask.CompletedTask;
        }

        public void Close() => _closed = true;
    }

    [Fact]
    public async Task Network_SimulateTenConcurrentParticipants_AndRejectEleventh()
    {
        var pool = new VirtualIpPool(subnetCidr: "10.77.0.0/24", serverVirtualIp: "10.77.0.1", maxUsers: 10);
        var router = new HubAndSpokeRouter(pool);

        var peers = new List<SimulatedPeer>();

        // Connect 10 users (max allowed)
        for (ushort id = 1; id <= 10; id++)
        {
            bool allocated = pool.TryAllocate(id, 0, out uint ip);
            Assert.True(allocated);

            var peer = new SimulatedPeer
            {
                PeerId = id,
                VirtualIp = ip,
                Hostname = $"User-{id}-PC"
            };
            peers.Add(peer);
            router.RegisterPeer(peer);
        }

        Assert.Equal(10, router.ActivePeerCount);
        Assert.Equal(10, pool.ActiveCount);

        // 11th user must be rejected
        bool overflowAllocated = pool.TryAllocate(11, 0, out uint overflowIp);
        Assert.False(overflowAllocated);
        Assert.Equal(0u, overflowIp);

        // Host (Peer #1) broadcasts a LAN game discovery packet
        byte[] discoveryPayload = new byte[32];
        discoveryPayload[0] = 0x45; // IPv4
        discoveryPayload[8] = 64;   // TTL
        discoveryPayload[9] = 17;   // UDP
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(discoveryPayload.AsSpan(2, 2), 32);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(discoveryPayload.AsSpan(12, 4), peers[0].VirtualIp);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(discoveryPayload.AsSpan(16, 4), 0xFFFFFFFF); // Limited broadcast

        ushort csum = IpPacketParser.CalculateChecksum(discoveryPayload.AsSpan(0, 20));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(discoveryPayload.AsSpan(10, 2), csum);

        await router.RoutePacketAsync(discoveryPayload, peers[0]);

        // Verify that all 9 other peers received the packet
        for (int i = 1; i < 10; i++)
        {
            Assert.Single(peers[i].Inbox);
        }
        // Sender must not receive it
        Assert.Empty(peers[0].Inbox);
    }
}
