namespace TechnicsControl.Core.Race;

/// <summary>RACE パケットの種別（2 バイト目）。</summary>
public static class RaceType
{
    public const byte Command = 0x5A;
    public const byte Response = 0x5B;
    public const byte CommandNoResponse = 0x5C;
    public const byte Indication = 0x5D;
}

/// <summary>
/// Airoha RACE パケット。
/// <c>head | type | length(LE16) = payload長+2 | id(LE16) | payload</c>
/// Payload は全体の 6 バイト目以降。応答（5B）では Payload[0] がステータス。
/// </summary>
public sealed class RacePacket
{
    public const byte DefaultHead = 0x05;
    public const int HeaderSize = 6;

    public RacePacket(byte type, ushort id, byte[]? payload = null, byte head = DefaultHead)
    {
        Head = head;
        Type = type;
        Id = id;
        Payload = payload ?? [];
    }

    public byte Head { get; }
    public byte Type { get; }
    public ushort Id { get; }
    public byte[] Payload { get; }

    /// <summary>応答のステータス（0 が成功）。Payload が空なら 0xFF。</summary>
    public byte Status => Payload.Length > 0 ? Payload[0] : (byte)0xFF;

    /// <summary>
    /// 公式アプリの解析コードと同じく、パケット全体での位置で 1 バイト読む（6 以上）。
    /// </summary>
    public byte At(int packetIndex) => Payload[packetIndex - HeaderSize];

    public int PacketLength => HeaderSize + Payload.Length;

    public static RacePacket Request(ushort id, params byte[] payload) => new(RaceType.Command, id, payload);

    public byte[] ToBytes()
    {
        var length = Payload.Length + 2;
        var bytes = new byte[HeaderSize + Payload.Length];
        bytes[0] = Head;
        bytes[1] = Type;
        bytes[2] = (byte)length;
        bytes[3] = (byte)(length >> 8);
        bytes[4] = (byte)Id;
        bytes[5] = (byte)(Id >> 8);
        Payload.CopyTo(bytes, HeaderSize);
        return bytes;
    }

    /// <summary>完全な 1 パケット分のバイト列から作る。</summary>
    public static RacePacket Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
            throw new FormatException("RACE パケットが短すぎます。");
        var length = bytes[2] | (bytes[3] << 8);
        if (length < 2 || bytes.Length < 4 + length)
            throw new FormatException("RACE パケットの長さが不正です。");
        var id = (ushort)(bytes[4] | (bytes[5] << 8));
        return new RacePacket(bytes[1], id, bytes.Slice(HeaderSize, length - 2).ToArray(), bytes[0]);
    }

    public override string ToString() => string.Join(' ', ToBytes().Select(b => b.ToString("X2")));
}
