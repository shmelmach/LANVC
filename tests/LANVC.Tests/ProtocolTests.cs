using System.Text;
using LANVC.Shared.Protocol;
using Xunit;

namespace LANVC.Tests;

public class ProtocolTests
{
    [Fact]
    public void FrameSerializer_PackAndUnpack_MatchesOriginalData()
    {
        byte[] payload = Encoding.UTF8.GetBytes("Test LANVC Payload 12345");
        byte[] frame = FrameSerializer.Pack(MessageType.DataPacket, payload);

        Assert.True(FrameSerializer.TryUnpack(frame, out var type, out var unpackedPayload));
        Assert.Equal(MessageType.DataPacket, type);
        Assert.Equal(payload, unpackedPayload.ToArray());
    }

    [Fact]
    public void HandshakeRequest_SerializationRoundtrip_PreservesAllFields()
    {
        var original = new HandshakeRequest
        {
            ProtocolVersion = 1,
            ClientName = "Denis-Gaming-PC",
            Password = "MySecretLanPassword2026",
            PreferredVirtualIp = 0x0A4D0005 // 10.77.0.5
        };

        using var ms = new MemoryStream();
        original.Serialize(ms);

        var deserialized = HandshakeRequest.Deserialize(ms.ToArray());

        Assert.Equal(original.ProtocolVersion, deserialized.ProtocolVersion);
        Assert.Equal(original.ClientName, deserialized.ClientName);
        Assert.Equal(original.Password, deserialized.Password);
        Assert.Equal(original.PreferredVirtualIp, deserialized.PreferredVirtualIp);
    }

    [Fact]
    public void HandshakeResponse_SerializationRoundtrip_PreservesAllFields()
    {
        var original = new HandshakeResponse
        {
            Status = HandshakeStatusCode.Success,
            AssignedVirtualIp = 0x0A4D0002, // 10.77.0.2
            SubnetMask = 0xFFFFFF00,        // 255.255.255.0
            ServerVirtualIp = 0x0A4D0001,   // 10.77.0.1
            PeerId = 42,
            KeepaliveSeconds = 20,
            Message = "Welcome to LANVC!"
        };

        using var ms = new MemoryStream();
        original.Serialize(ms);

        var deserialized = HandshakeResponse.Deserialize(ms.ToArray());

        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.AssignedVirtualIp, deserialized.AssignedVirtualIp);
        Assert.Equal(original.SubnetMask, deserialized.SubnetMask);
        Assert.Equal(original.ServerVirtualIp, deserialized.ServerVirtualIp);
        Assert.Equal(original.PeerId, deserialized.PeerId);
        Assert.Equal(original.KeepaliveSeconds, deserialized.KeepaliveSeconds);
        Assert.Equal(original.Message, deserialized.Message);
    }

    [Fact]
    public void PeerListUpdate_SerializationRoundtrip_PreservesPeers()
    {
        var update = new PeerListUpdate();
        update.Peers.Add(new PeerDto { PeerId = 1, Hostname = "Alex-PC", VirtualIp = 0x0A4D0002, LatencyMs = 15, OnlineDurationSeconds = 120, IsOnline = true });
        update.Peers.Add(new PeerDto { PeerId = 2, Hostname = "Max-PC", VirtualIp = 0x0A4D0003, LatencyMs = 25, OnlineDurationSeconds = 60, IsOnline = true });

        using var ms = new MemoryStream();
        update.Serialize(ms);

        var deserialized = PeerListUpdate.Deserialize(ms.ToArray());

        Assert.Equal(2, deserialized.Peers.Count);
        Assert.Equal("Alex-PC", deserialized.Peers[0].Hostname);
        Assert.Equal("10.77.0.2", deserialized.Peers[0].VirtualIpString);
        Assert.Equal("Max-PC", deserialized.Peers[1].Hostname);
        Assert.Equal("10.77.0.3", deserialized.Peers[1].VirtualIpString);
    }

    [Fact]
    public void GameDiscoveryMessage_SerializationRoundtrip_PreservesPayload()
    {
        string motd = "[MOTD]Survival World[/MOTD][AD]25565[/AD]";
        byte[] rawPayload = Encoding.UTF8.GetBytes(motd);

        var msg = new GameDiscoveryMessage
        {
            TargetPort = 4445,
            Payload = rawPayload
        };

        using var ms = new MemoryStream();
        msg.Serialize(ms);

        var deserialized = GameDiscoveryMessage.Deserialize(ms.ToArray());

        Assert.Equal(4445, deserialized.TargetPort);
        Assert.Equal(motd, Encoding.UTF8.GetString(deserialized.Payload));
    }
}
