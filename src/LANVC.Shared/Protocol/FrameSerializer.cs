namespace LANVC.Shared.Protocol;

public static class FrameSerializer
{
    public static byte[] Pack(MessageType type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payload), "Payload size exceeds maximum allowed frame length (65535 bytes)");

        byte[] frame = new byte[FrameHeader.HeaderSize + payload.Length];
        var header = new FrameHeader((byte)type, (ushort)payload.Length);
        header.WriteTo(frame.AsSpan(0, FrameHeader.HeaderSize));
        payload.CopyTo(frame.AsSpan(FrameHeader.HeaderSize));
        return frame;
    }

    public static byte[] PackDataPacket(ReadOnlySpan<byte> ipPacket)
    {
        return Pack(MessageType.DataPacket, ipPacket);
    }

    public static bool TryUnpack(ReadOnlySpan<byte> rawFrame, out MessageType type, out ReadOnlySpan<byte> payload)
    {
        type = MessageType.None;
        payload = default;

        if (!FrameHeader.TryParse(rawFrame, out var header))
            return false;

        if (rawFrame.Length < FrameHeader.HeaderSize + header.PayloadLength)
            return false;

        type = (MessageType)header.Type;
        payload = rawFrame.Slice(FrameHeader.HeaderSize, header.PayloadLength);
        return true;
    }
}
