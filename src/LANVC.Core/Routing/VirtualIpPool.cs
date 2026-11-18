using System.Net;
using LANVC.Core.Routing;

namespace LANVC.Core.Routing;

public sealed class VirtualIpPool
{
    private readonly uint _baseNetwork; // e.g. 10.77.0.0
    private readonly uint _subnetMask;  // e.g. 255.255.255.0
    private readonly uint _serverIp;    // e.g. 10.77.0.1
    private readonly uint _broadcastIp; // e.g. 10.77.0.255
    private readonly int _maxUsers;
    private readonly object _lock = new();

    private readonly HashSet<uint> _allocatedIps = new();
    private readonly Dictionary<ushort, uint> _peerAllocations = new();

    public uint SubnetMask => _subnetMask;
    public uint ServerIp => _serverIp;
    public uint SubnetBroadcast => _broadcastIp;
    public int MaxUsers => _maxUsers;
    public int ActiveCount
    {
        get
        {
            lock (_lock)
                return _allocatedIps.Count;
        }
    }

    public VirtualIpPool(string subnetCidr = "10.77.0.0/24", string serverVirtualIp = "10.77.0.1", int maxUsers = 10)
    {
        _maxUsers = Math.Clamp(maxUsers, 1, 250);

        string[] parts = subnetCidr.Split('/');
        uint networkIp = IpPacketParser.FromIpString(parts[0]);
        int prefix = parts.Length > 1 && int.TryParse(parts[1], out int p) ? p : 24;

        _subnetMask = prefix switch
        {
            24 => 0xFFFFFF00,
            16 => 0xFFFF0000,
            _ => 0xFFFFFF00
        };

        _baseNetwork = networkIp & _subnetMask;
        _broadcastIp = _baseNetwork | ~_subnetMask;
        _serverIp = IpPacketParser.FromIpString(serverVirtualIp);

        if (_serverIp == 0 || (_serverIp & _subnetMask) != _baseNetwork)
        {
            _serverIp = _baseNetwork + 1; // 10.77.0.1
        }
    }

    public bool TryAllocate(ushort peerId, uint requestedIp, out uint allocatedIp)
    {
        lock (_lock)
        {
            if (_peerAllocations.TryGetValue(peerId, out uint existing))
            {
                allocatedIp = existing;
                return true;
            }

            if (_allocatedIps.Count >= _maxUsers)
            {
                allocatedIp = 0;
                return false;
            }

            // Check if requested IP is valid and available
            if (requestedIp != 0 &&
                (requestedIp & _subnetMask) == _baseNetwork &&
                requestedIp != _serverIp &&
                requestedIp != _broadcastIp &&
                !_allocatedIps.Contains(requestedIp))
            {
                _allocatedIps.Add(requestedIp);
                _peerAllocations[peerId] = requestedIp;
                allocatedIp = requestedIp;
                return true;
            }

            // Search for next free IP starting from .2 up to .254
            uint startHost = 2;
            uint maxHost = 254;

            for (uint host = startHost; host <= maxHost; host++)
            {
                uint candidate = _baseNetwork + host;
                if (candidate == _serverIp || candidate == _broadcastIp)
                    continue;

                if (!_allocatedIps.Contains(candidate))
                {
                    _allocatedIps.Add(candidate);
                    _peerAllocations[peerId] = candidate;
                    allocatedIp = candidate;
                    return true;
                }
            }

            allocatedIp = 0;
            return false;
        }
    }

    public void Release(ushort peerId)
    {
        lock (_lock)
        {
            if (_peerAllocations.Remove(peerId, out uint ip))
            {
                _allocatedIps.Remove(ip);
            }
        }
    }

    public bool IsIpAllocated(uint ip)
    {
        lock (_lock)
            return _allocatedIps.Contains(ip) || ip == _serverIp;
    }
}
