namespace TechnicsControl.Core.Race;

/// <summary>受信したバイト列を RACE パケット単位に切り出す。</summary>
public sealed class RaceFramer
{
    private readonly List<byte> _buffer = [];

    public IEnumerable<RacePacket> Push(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data.ToArray());
        var packets = new List<RacePacket>();
        while (_buffer.Count >= 4)
        {
            // 先頭が RACE のヘッダでなければ 1 バイトずつ読み捨てて同期し直す
            if (_buffer[0] is not (0x05 or 0x15))
            {
                _buffer.RemoveAt(0);
                continue;
            }
            var length = _buffer[2] | (_buffer[3] << 8);
            if (length < 2)
            {
                _buffer.RemoveAt(0);
                continue;
            }
            var total = 4 + length;
            if (_buffer.Count < total)
                break;
            packets.Add(RacePacket.Parse(_buffer.GetRange(0, total).ToArray()));
            _buffer.RemoveRange(0, total);
        }
        return packets;
    }
}
