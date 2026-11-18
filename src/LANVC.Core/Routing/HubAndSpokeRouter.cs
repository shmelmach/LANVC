using System.Buffers.Binary;
using System.Collections.Concurrent;
using LANVC.Core.Sessions;
using LANVC.Shared.Protocol;

namespace LANVC.Core.Routing;

public sealed class HubAndSpokeRouter
{
    private readonly VirtualIpPool _ipPool;
    private readonly ConcurrentDictionary<uint, IPeerSession> _sessionsByIp = new();
    private readonly ConcurrentDictionary<ushort, IPeerSession> _sessionsById = new();

    public VirtualIpPool IpPool => _ipPool;
    public int ActivePeerCount => _sessionsById.Count;

    public event Action<string>? OnLog;

    public HubAndSpokeRouter(VirtualIpPool ipPool)
    {
        _ipPool = ipPool;
    }

    public bool RegisterPeer(IPeerSession session)
    {
        if (_sessionsById.TryAdd(session.PeerId, session))
        {
            _sessionsByIp[session.VirtualIp] = session;
            OnLog?.Invoke($"[Router] Peer #{session.PeerId} ({session.Hostname}) registered with IP {IpPacketParser.ToIpString(session.VirtualIp)}");
            return true;
        }
        return false;
    }

    public void UnregisterPeer(ushort peerId)
    {
        if (_sessionsById.TryRemove(peerId, out var session))
        {
            _sessionsByIp.TryRemove(session.VirtualIp, out _);
            _ipPool.Release(peerId);
            OnLog?.Invoke($"[Router] Peer #{peerId} ({session.Hostname}) unregistered");
        }
    }

    public IReadOnlyCollection<IPeerSession> GetAllPeers() => _sessionsById.Values.ToArray();

    public async ValueTask RoutePacketAsync(ReadOnlyMemory<byte> rawPacket, IPeerSession sender, CancellationToken ct = default)
    {
        var span = rawPacket.Span;
        if (!IpPacketParser.IsValidIPv4(span))
            return;

        uint srcIp = IpPacketParser.GetSourceIp(span);
        uint dstIp = IpPacketParser.GetDestinationIp(span);

        // Anti-spoofing protection: packet source IP must match sender's virtual IP
        if (srcIp != sender.VirtualIp)
        {
            OnLog?.Invoke($"[Security] Anti-Spoofing: Dropped packet from Peer #{sender.PeerId} claiming IP {IpPacketParser.ToIpString(srcIp)} instead of {IpPacketParser.ToIpString(sender.VirtualIp)}");
            return;
        }

        // Pack frame once for transmission
        byte[] frame = FrameSerializer.PackDataPacket(span);

        // 1. Broadcast check (Subnet broadcast e.g. 10.77.0.255 or Limited broadcast 255.255.255.255)
        if (IpPacketParser.IsBroadcast(dstIp, _ipPool.SubnetBroadcast))
        {
            await BroadcastToAllAsync(frame, excludePeerId: sender.PeerId, ct);
            return;
        }

        // 2. Multicast check (224.0.0.0/4)
        if (IpPacketParser.IsMulticast(dstIp))
        {
            await BroadcastToAllAsync(frame, excludePeerId: sender.PeerId, ct);
            return;
        }

        // 3. Packet destined to server itself (10.77.0.1)
        if (dstIp == _ipPool.ServerIp)
        {
            HandleServerDestinedPacket(span, sender);
            return;
        }

        // 4. Unicast forwarding to specific peer
        if (_sessionsByIp.TryGetValue(dstIp, out var destinationSession))
        {
            if (destinationSession.IsActive)
            {
                await destinationSession.SendDataPacketAsync(frame, ct);
            }
        }
        else
        {
            // Destination IP not found in virtual subnet - drop
        }
    }

    public async ValueTask BroadcastToAllAsync(ReadOnlyMemory<byte> frame, ushort excludePeerId = 0, CancellationToken ct = default)
    {
        foreach (var session in _sessionsById.Values)
        {
            if (session.PeerId == excludePeerId || !session.IsActive)
                continue;

            try
            {
                await session.SendDataPacketAsync(frame, ct);
            }
            catch
            {
                // Ignore transient send failures in broadcast
            }
        }
    }

    private void HandleServerDestinedPacket(ReadOnlySpan<byte> span, IPeerSession sender)
    {
        byte protocol = IpPacketParser.GetProtocol(span);
        if (protocol == IpPacketParser.ProtocolIcmp)
        {
            // Reply to ICMP Echo Request (ping)
            int ihl = (span[0] & 0x0F) * 4;
            if (span.Length >= ihl + 8)
            {
                byte icmpType = span[ihl];
                if (icmpType == 8) // Echo Request
                {
                    byte[] reply = span.ToArray();
                    // Swap src and dst
                    BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(12, 4), _ipPool.ServerIp);
                    BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(16, 4), sender.VirtualIp);

                    // Set ICMP Type = 0 (Echo Reply)
                    reply[ihl] = 0;

                    // Recalculate ICMP checksum
                    reply[ihl + 2] = 0;
                    reply[ihl + 3] = 0;
                    ushort icmpCsum = IpPacketParser.CalculateChecksum(reply.AsSpan(ihl));
                    BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(ihl + 2, 2), icmpCsum);

                    // Recalculate IP header checksum
                    reply[10] = 0;
                    reply[11] = 0;
                    ushort ipCsum = IpPacketParser.CalculateChecksum(reply.AsSpan(0, ihl));
                    BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(10, 2), ipCsum);

                    byte[] frame = FrameSerializer.PackDataPacket(reply);
                    _ = sender.SendDataPacketAsync(frame);
                }
            }
        }
    }
}
