using System.Buffers.Binary;

namespace LANVC.Shared.Protocol;

public readonly struct FrameHeader
{
    public const byte Magic1 = 0x4C; // 'L'
    public const byte Magic2 = 0x56; // 'V'
    public const int HeaderSize = 5;

    public byte Type { get; }
    public ushort PayloadLength { get; }

    public FrameHeader(byte type, ushort payloadLength)
    {
        Type = type;
        PayloadLength = payloadLength;
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
            throw new ArgumentException("Destination buffer is too small for FrameHeader");

        destination[0] = Magic1;
        destination[1] = Magic2;
        destination[2] = Type;
        BinaryPrimitives.WriteUInt16BigEndian(destination[3..5], PayloadLength);
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out FrameHeader header)
    {
        header = default;
        if (source.Length < HeaderSize)
            return false;

        if (source[0] != Magic1 || source[1] != Magic2)
            return false;

        byte type = source[2];
        ushort len = BinaryPrimitives.ReadUInt16BigEndian(source[3..5]);
        header = new FrameHeader(type, len);
        return true;
    }
}
