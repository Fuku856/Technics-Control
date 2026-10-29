using System.Buffers.Binary;
using System.Text;

namespace AssetExtractor;

/// <summary>
/// resources.arsc から、drawable／mipmap の名前と APK 内のファイル（難読化された名前）の対応を読む。
/// tools/analysis/arsc.py と同じ処理。
/// </summary>
internal sealed class ResourceTable
{
    public sealed record Entry(string Name, string Type, string Path, int Density);

    public List<Entry> Entries { get; } = [];

    public static ResourceTable Parse(byte[] data)
    {
        var table = new ResourceTable();
        var headerSize = U16(data, 2);
        var (globalStrings, offset) = ReadPool(data, headerSize);
        while (offset < data.Length)
        {
            var chunkType = U16(data, offset);
            var chunkSize = (int)U32(data, offset + 4);
            if (chunkType != 0x0200)
            {
                offset += chunkSize;
                continue;
            }
            var packageEnd = offset + chunkSize;
            var packageHeaderSize = U16(data, offset + 2);
            var typeStringsOffset = (int)U32(data, offset + 8 + 4 + 256);
            var keyStringsOffset = (int)U32(data, offset + 8 + 4 + 256 + 8);
            var (types, _) = ReadPool(data, offset + typeStringsOffset);
            var (keys, _) = ReadPool(data, offset + keyStringsOffset);

            var p = offset + packageHeaderSize;
            while (p < packageEnd)
            {
                var t = U16(data, p);
                var h = U16(data, p + 2);
                var s = (int)U32(data, p + 4);
                if (t == 0x0201)
                    table.ReadTypeChunk(data, p, h, types, keys, globalStrings);
                p += s;
            }
            offset = packageEnd;
        }
        return table;
    }

    private void ReadTypeChunk(byte[] d, int p, int headerSize, List<string> types, List<string> keys, List<string> globalStrings)
    {
        var typeId = d[p + 8];
        var flags = d[p + 9];
        var count = (int)U32(d, p + 12);
        var entriesStart = (int)U32(d, p + 16);
        var configSize = (int)U32(d, p + 20);
        var density = configSize >= 16 ? U16(d, p + 20 + 14) : 0;
        var typeName = types[typeId - 1];
        if (typeName is not ("drawable" or "mipmap"))
            return;

        var sparse = (flags & 0x01) != 0;
        for (var i = 0; i < count; i++)
        {
            int entryOffset;
            if (sparse)
            {
                entryOffset = U16(d, p + headerSize + 4 * i + 2) * 4;
            }
            else
            {
                var raw = U32(d, p + headerSize + 4 * i);
                if (raw == 0xFFFFFFFF)
                    continue;
                entryOffset = (int)raw;
            }
            var e = p + entriesStart + entryOffset;
            var entrySize = U16(d, e);
            var entryFlags = U16(d, e + 2);
            var key = keys[(int)U32(d, e + 4)];
            if ((entryFlags & 1) != 0)
                continue; // 複合リソース（色の組み合わせなど）は対象外
            var valueType = d[e + entrySize + 3];
            var valueData = (int)U32(d, e + entrySize + 4);
            if (valueType == 3)
                Entries.Add(new Entry(key, typeName, globalStrings[valueData], density));
        }
    }

    /// <summary>名前に対応するファイルのうち、最も解像度の高いもの。</summary>
    public Entry? Best(string name) => Entries
        .Where(e => e.Name == name && !e.Path.EndsWith(".xml", StringComparison.Ordinal))
        .OrderByDescending(e => e.Density is 0xFFFE or 0xFFFF ? 0 : e.Density)
        .FirstOrDefault();

    private static (List<string> Strings, int End) ReadPool(byte[] d, int offset)
    {
        var headerSize = U16(d, offset + 2);
        var size = (int)U32(d, offset + 4);
        var count = (int)U32(d, offset + 8);
        var flags = U32(d, offset + 16);
        var stringsStart = (int)U32(d, offset + 20);
        var utf8 = (flags & 0x100) != 0;
        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var p = offset + stringsStart + (int)U32(d, offset + headerSize + 4 * i);
            if (utf8)
            {
                p += (d[p] & 0x80) != 0 ? 2 : 1; // UTF-16 での長さ（読み飛ばす）
                int length = d[p];
                if ((length & 0x80) != 0)
                {
                    length = ((length & 0x7F) << 8) | d[p + 1];
                    p += 2;
                }
                else
                {
                    p += 1;
                }
                result.Add(Encoding.UTF8.GetString(d, p, length));
            }
            else
            {
                int length = U16(d, p);
                p += 2;
                if ((length & 0x8000) != 0)
                {
                    length = ((length & 0x7FFF) << 16) | U16(d, p);
                    p += 2;
                }
                result.Add(Encoding.Unicode.GetString(d, p, length * 2));
            }
        }
        return (result, offset + size);
    }

    private static ushort U16(byte[] d, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(offset));

    private static uint U32(byte[] d, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(offset));
}
