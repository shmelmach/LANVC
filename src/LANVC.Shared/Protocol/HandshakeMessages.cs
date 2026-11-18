using System.Buffers.Binary;
using System.Text;

namespace LANVC.Shared.Protocol;

public sealed class HandshakeRequest
{
    public ushort ProtocolVersion { get; set; } = 1;
    public string ClientName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public uint PreferredVirtualIp { get; set; }

    public void Serialize(Stream destination)
    {
        Span<byte> buf = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(buf[0..2], ProtocolVersion);
        BinaryPrimitives.WriteUInt32BigEndian(buf[2..6], PreferredVirtualIp);
        destination.Write(buf);

        byte[] nameBytes = Encoding.UTF8.GetBytes(ClientName);
        destination.WriteByte((byte)Math.Min(nameBytes.Length, 255));
        destination.Write(nameBytes, 0, Math.Min(nameBytes.Length, 255));

        byte[] pwdBytes = Encoding.UTF8.GetBytes(Password);
        destination.WriteByte((byte)Math.Min(pwdBytes.Length, 255));
        destination.Write(pwdBytes, 0, Math.Min(pwdBytes.Length, 255));
    }

    public static HandshakeRequest Deserialize(ReadOnlySpan<byte> source)
    {
        var req = new HandshakeRequest();
        if (source.Length < 6)
            return req;

        req.ProtocolVersion = BinaryPrimitives.ReadUInt16BigEndian(source[0..2]);
        req.PreferredVirtualIp = BinaryPrimitives.ReadUInt32BigEndian(source[2..6]);

        int offset = 6;
        if (offset < source.Length)
        {
            byte nameLen = source[offset++];
            if (offset + nameLen <= source.Length)
            {
                req.ClientName = Encoding.UTF8.GetString(source.Slice(offset, nameLen));
                offset += nameLen;
            }
        }

        if (offset < source.Length)
        {
            byte pwdLen = source[offset++];
            if (offset + pwdLen <= source.Length)
            {
                req.Password = Encoding.UTF8.GetString(source.Slice(offset, pwdLen));
            }
        }

        return req;
    }
}

public sealed class HandshakeResponse
{
    public HandshakeStatusCode Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public uint AssignedVirtualIp { get; set; }
    public uint SubnetMask { get; set; }
    public uint ServerVirtualIp { get; set; }
    public ushort PeerId { get; set; }
    public ushort KeepaliveSeconds { get; set; } = 15;

    public void Serialize(Stream destination)
    {
        destination.WriteByte((byte)Status);

        Span<byte> buf = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(buf[0..4], AssignedVirtualIp);
        BinaryPrimitives.WriteUInt32BigEndian(buf[4..8], SubnetMask);
        BinaryPrimitives.WriteUInt32BigEndian(buf[8..12], ServerVirtualIp);
        BinaryPrimitives.WriteUInt16BigEndian(buf[12..14], PeerId);
        BinaryPrimitives.WriteUInt16BigEndian(buf[14..16], KeepaliveSeconds);
        destination.Write(buf);

        byte[] msgBytes = Encoding.UTF8.GetBytes(Message);
        destination.WriteByte((byte)Math.Min(msgBytes.Length, 255));
        destination.Write(msgBytes, 0, Math.Min(msgBytes.Length, 255));
    }

    public static HandshakeResponse Deserialize(ReadOnlySpan<byte> source)
    {
        var resp = new HandshakeResponse();
        if (source.Length < 17)
            return resp;

        resp.Status = (HandshakeStatusCode)source[0];
        resp.AssignedVirtualIp = BinaryPrimitives.ReadUInt32BigEndian(source[1..5]);
        resp.SubnetMask = BinaryPrimitives.ReadUInt32BigEndian(source[5..9]);
        resp.ServerVirtualIp = BinaryPrimitives.ReadUInt32BigEndian(source[9..13]);
        resp.PeerId = BinaryPrimitives.ReadUInt16BigEndian(source[13..15]);
        resp.KeepaliveSeconds = BinaryPrimitives.ReadUInt16BigEndian(source[15..17]);

        if (source.Length > 17)
        {
            byte msgLen = source[17];
            if (18 + msgLen <= source.Length)
            {
                resp.Message = Encoding.UTF8.GetString(source.Slice(18, msgLen));
            }
        }

        return resp;
    }
}
