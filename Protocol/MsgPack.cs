using System.Buffers.Binary;
using System.Text;

static class MsgPack
{
    public sealed record Raw(byte[] Bytes);
    public sealed record Binary(byte[] Bytes);
    public sealed record Ext(sbyte Code, byte[] Bytes);

    public static byte[] Encode(object? value)
    {
        using var ms = new MemoryStream();
        Write(ms, value);
        return ms.ToArray();
    }

    public static object? DecodePrefix(byte[] bytes, out int consumed)
    {
        consumed = 0;
        return Read(bytes, ref consumed);
    }

    public static object? Decode(byte[] bytes)
    {
        var offset = 0;
        return Read(bytes, ref offset);
    }

    public static object?[] DecodeAll(byte[] bytes)
    {
        var offset = 0;
        var values = new List<object?>();
        while (offset < bytes.Length)
        {
            var before = offset;
            values.Add(Read(bytes, ref offset));
            if (offset <= before)
            {
                break;
            }
        }
        return values.ToArray();
    }

    static void Write(Stream stream, object? value)
    {
        switch (value)
        {
            case null:
                stream.WriteByte(0xc0);
                break;
            case bool boolean:
                stream.WriteByte(boolean ? (byte)0xc3 : (byte)0xc2);
                break;
            case string text:
                WriteString(stream, text);
                break;
            case Raw raw:
                stream.Write(raw.Bytes);
                break;
            case Binary binary:
                WriteBinary(stream, binary.Bytes);
                break;
            case Ext ext:
                WriteExt(stream, ext.Code, ext.Bytes);
                break;
            case DateTime dateTime:
                WriteDateTime(stream, dateTime);
                break;
            case int i:
                WriteInteger(stream, i);
                break;
            case byte or sbyte or short or ushort or uint:
                WriteInteger(stream, Convert.ToInt64(value));
                break;
            case long l:
                WriteInteger(stream, l);
                break;
            case ulong number when number <= long.MaxValue:
                WriteInteger(stream, (long)number);
                break;
            case ulong number:
                stream.WriteByte(0xcf);
                Span<byte> unsigned = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64BigEndian(unsigned, number);
                stream.Write(unsigned);
                break;
            case double d:
                WriteDouble(stream, d);
                break;
            case float f:
                WriteDouble(stream, f);
                break;
            case object?[] array:
                WriteArrayHeader(stream, array.Length);
                foreach (var item in array) Write(stream, item);
                break;
            case Array array:
                WriteArrayHeader(stream, array.Length);
                foreach (var item in array) Write(stream, item);
                break;
            case Dictionary<string, object?> map:
                WriteMapHeader(stream, map.Count);
                foreach (var (key, item) in map)
                {
                    WriteString(stream, key);
                    Write(stream, item);
                }
                break;
            case Dictionary<int, object?> numericMap:
                WriteMapHeader(stream, numericMap.Count);
                foreach (var (key, item) in numericMap)
                {
                    WriteInteger(stream, key);
                    Write(stream, item);
                }
                break;
            default:
                WriteString(stream, value.ToString() ?? "");
                break;
        }
    }

    static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= 31)
        {
            stream.WriteByte((byte)(0xa0 | bytes.Length));
        }
        else if (bytes.Length <= byte.MaxValue)
        {
            stream.WriteByte(0xd9);
            stream.WriteByte((byte)bytes.Length);
        }
        else
        {
            stream.WriteByte(0xda);
            Span<byte> len = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)bytes.Length);
            stream.Write(len);
        }
        stream.Write(bytes);
    }

    static void WriteBinary(Stream stream, byte[] bytes)
    {
        if (bytes.Length <= byte.MaxValue)
        {
            stream.WriteByte(0xc4);
            stream.WriteByte((byte)bytes.Length);
        }
        else if (bytes.Length <= ushort.MaxValue)
        {
            stream.WriteByte(0xc5);
            Span<byte> len = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)bytes.Length);
            stream.Write(len);
        }
        else
        {
            stream.WriteByte(0xc6);
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(len, (uint)bytes.Length);
            stream.Write(len);
        }

        stream.Write(bytes);
    }

    static void WriteExt(Stream stream, sbyte code, byte[] bytes)
    {
        switch (bytes.Length)
        {
            case 1:
                stream.WriteByte(0xd4);
                break;
            case 2:
                stream.WriteByte(0xd5);
                break;
            case 4:
                stream.WriteByte(0xd6);
                break;
            case 8:
                stream.WriteByte(0xd7);
                break;
            case 16:
                stream.WriteByte(0xd8);
                break;
            default:
                if (bytes.Length <= byte.MaxValue)
                {
                    stream.WriteByte(0xc7);
                    stream.WriteByte((byte)bytes.Length);
                }
                else if (bytes.Length <= ushort.MaxValue)
                {
                    stream.WriteByte(0xc8);
                    Span<byte> len = stackalloc byte[2];
                    BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)bytes.Length);
                    stream.Write(len);
                }
                else
                {
                    stream.WriteByte(0xc9);
                    Span<byte> len = stackalloc byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(len, (uint)bytes.Length);
                    stream.Write(len);
                }
                break;
        }

        stream.WriteByte(unchecked((byte)code));
        stream.Write(bytes);
    }

    static void WriteArrayHeader(Stream stream, int count)
    {
        if (count <= 15)
        {
            stream.WriteByte((byte)(0x90 | count));
            return;
        }
        stream.WriteByte(0xdc);
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)count);
        stream.Write(len);
    }

    static void WriteMapHeader(Stream stream, int count)
    {
        if (count <= 15)
        {
            stream.WriteByte((byte)(0x80 | count));
            return;
        }
        stream.WriteByte(0xde);
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)count);
        stream.Write(len);
    }

    static void WriteInteger(Stream stream, long value)
    {
        if (value >= 0 && value <= 127)
        {
            stream.WriteByte((byte)value);
        }
        else if (value >= byte.MinValue && value <= byte.MaxValue)
        {
            stream.WriteByte(0xcc);
            stream.WriteByte((byte)value);
        }
        else if (value >= short.MinValue && value <= short.MaxValue)
        {
            stream.WriteByte(0xd1);
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(bytes, (short)value);
            stream.Write(bytes);
        }
        else if (value >= int.MinValue && value <= int.MaxValue)
        {
            stream.WriteByte(0xd2);
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, (int)value);
            stream.Write(bytes);
        }
        else
        {
            stream.WriteByte(0xd3);
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            stream.Write(bytes);
        }
    }

    static void WriteDateTime(Stream stream, DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        var seconds = new DateTimeOffset(utc).ToUnixTimeSeconds();
        if (seconds >= 0 && seconds <= uint.MaxValue)
        {
            stream.WriteByte(0xd6);
            stream.WriteByte(0xff);
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)seconds);
            stream.Write(bytes);
            return;
        }

        stream.WriteByte(0xc7);
        stream.WriteByte(12);
        stream.WriteByte(0xff);
        Span<byte> payload = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(payload[..4], 0);
        BinaryPrimitives.WriteInt64BigEndian(payload[4..], seconds);
        stream.Write(payload);
    }

    static void WriteDouble(Stream stream, double value)
    {
        stream.WriteByte(0xcb);
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
        stream.Write(bytes);
    }

    static object? Read(byte[] bytes, ref int offset)
    {
        if (offset >= bytes.Length) return null;

        var marker = bytes[offset++];
        if (marker <= 0x7f) return marker;
        if (marker >= 0xe0) return unchecked((sbyte)marker);
        if ((marker & 0xe0) == 0xa0) return ReadString(bytes, ref offset, marker & 0x1f);
        if ((marker & 0xf0) == 0x90) return ReadArray(bytes, ref offset, marker & 0x0f);
        if ((marker & 0xf0) == 0x80) return ReadMap(bytes, ref offset, marker & 0x0f);

        return marker switch
        {
            0xc0 => null,
            0xc2 => false,
            0xc3 => true,
            0xc4 => ReadBinary(bytes, ref offset, ReadByte(bytes, ref offset)),
            0xc5 => ReadBinary(bytes, ref offset, ReadUInt16(bytes, ref offset)),
            0xc6 => ReadBinary(bytes, ref offset, checked((int)ReadUInt32(bytes, ref offset))),
            0xca => ReadSingle(bytes, ref offset),
            0xcb => ReadDouble(bytes, ref offset),
            0xcc => ReadByte(bytes, ref offset),
            0xcd => ReadUInt16(bytes, ref offset),
            0xce => ReadUInt32(bytes, ref offset),
            0xcf => ReadUInt64(bytes, ref offset),
            0xd0 => unchecked((sbyte)ReadByte(bytes, ref offset)),
            0xd1 => ReadInt16(bytes, ref offset),
            0xd2 => ReadInt32(bytes, ref offset),
            0xd3 => ReadInt64(bytes, ref offset),
            0xd4 => ReadExt(bytes, ref offset, 1),
            0xd5 => ReadExt(bytes, ref offset, 2),
            0xd6 => ReadExt(bytes, ref offset, 4),
            0xd7 => ReadExt(bytes, ref offset, 8),
            0xd8 => ReadExt(bytes, ref offset, 16),
            0xd9 => ReadString(bytes, ref offset, ReadByte(bytes, ref offset)),
            0xda => ReadString(bytes, ref offset, ReadUInt16(bytes, ref offset)),
            0xdb => ReadString(bytes, ref offset, checked((int)ReadUInt32(bytes, ref offset))),
            0xc7 => ReadExt(bytes, ref offset, ReadByte(bytes, ref offset)),
            0xc8 => ReadExt(bytes, ref offset, ReadUInt16(bytes, ref offset)),
            0xc9 => ReadExt(bytes, ref offset, checked((int)ReadUInt32(bytes, ref offset))),
            0xdc => ReadArray(bytes, ref offset, ReadUInt16(bytes, ref offset)),
            0xdd => ReadArray(bytes, ref offset, checked((int)ReadUInt32(bytes, ref offset))),
            0xde => ReadMap(bytes, ref offset, ReadUInt16(bytes, ref offset)),
            0xdf => ReadMap(bytes, ref offset, checked((int)ReadUInt32(bytes, ref offset))),
            _ => throw new InvalidDataException($"不支持的 MessagePack 标记：{marker:x2}")
        };
    }

    static object? ReadExt(byte[] bytes, ref int offset, int length)
    {
        if (offset >= bytes.Length) return null;

        var type = unchecked((sbyte)bytes[offset++]);
        if (type == -1)
        {
            return ReadTimestampExt(bytes, ref offset, length);
        }

        var raw = ReadBinary(bytes, ref offset, length);
        return new Ext(type, raw);
    }

    static object? ReadTimestampExt(byte[] bytes, ref int offset, int length)
    {
        if (offset + length > bytes.Length)
        {
            offset = bytes.Length;
            return null;
        }

        try
        {
            DateTimeOffset value;
            switch (length)
            {
                case 4:
                    value = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
                    break;
                case 8:
                    var packed = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset, 8));
                    value = DateTimeOffset.FromUnixTimeSeconds(unchecked((long)(packed & 0x00000003ffffffffUL)));
                    break;
                case 12:
                    value = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(offset + 4, 8)));
                    break;
                default:
                    offset += length;
                    return null;
            }

            offset += length;
            return value.UtcDateTime;
        }
        catch
        {
            offset = Math.Min(bytes.Length, offset + length);
            return null;
        }
    }

    static object?[] ReadArray(byte[] bytes, ref int offset, int count)
    {
        if (count < 0 || count > bytes.Length - offset) throw new InvalidDataException("MessagePack 数组数据不完整");
        var result = new object?[count];
        for (var i = 0; i < count; i++)
        {
            if (offset >= bytes.Length) break;
            result[i] = Read(bytes, ref offset);
        }
        return result;
    }

    static Dictionary<string, object?> ReadMap(byte[] bytes, ref int offset, int count)
    {
        if (count < 0 || count > (bytes.Length - offset) / 2) throw new InvalidDataException("MessagePack 字典数据不完整");
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            if (offset >= bytes.Length) break;
            var key = Read(bytes, ref offset)?.ToString() ?? "";
            if (offset >= bytes.Length)
            {
                result[key] = null;
                break;
            }
            result[key] = Read(bytes, ref offset);
        }
        return result;
    }

    static string ReadString(byte[] bytes, ref int offset, int length)
    {
        if (length <= 0 || offset >= bytes.Length) return "";

        var available = Math.Min(length, bytes.Length - offset);
        var text = Encoding.UTF8.GetString(bytes, offset, available);
        offset += available;
        return text;
    }

    static byte[] ReadBinary(byte[] bytes, ref int offset, int length)
    {
        if (length <= 0 || offset >= bytes.Length) return Array.Empty<byte>();

        var available = Math.Min(length, bytes.Length - offset);
        var value = bytes.AsSpan(offset, available).ToArray();
        offset += available;
        return value;
    }

    static float ReadSingle(byte[] bytes, ref int offset)
    {
        if (offset + 4 > bytes.Length) throw new FormatException("MessagePack 浮点字段被截断");
        var value = BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    static double ReadDouble(byte[] bytes, ref int offset)
    {
        if (offset + 8 > bytes.Length) throw new FormatException("MessagePack 浮点字段被截断");
        var value = BinaryPrimitives.ReadDoubleBigEndian(bytes.AsSpan(offset, 8));
        offset += 8;
        return value;
    }

    static byte ReadByte(byte[] bytes, ref int offset)
    {
        if (offset >= bytes.Length) return 0;
        return bytes[offset++];
    }

    static ushort ReadUInt16(byte[] bytes, ref int offset)
    {
        if (offset + 2 > bytes.Length)
        {
            offset = bytes.Length;
            return 0;
        }

        var value = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
        offset += 2;
        return value;
    }

    static uint ReadUInt32(byte[] bytes, ref int offset)
    {
        if (offset + 4 > bytes.Length)
        {
            offset = bytes.Length;
            return 0;
        }

        var value = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    static short ReadInt16(byte[] bytes, ref int offset)
    {
        if (offset + 2 > bytes.Length)
        {
            offset = bytes.Length;
            return 0;
        }

        var value = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(offset, 2));
        offset += 2;
        return value;
    }

    static int ReadInt32(byte[] bytes, ref int offset)
    {
        if (offset + 4 > bytes.Length)
        {
            offset = bytes.Length;
            return 0;
        }

        var value = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    static long ReadInt64(byte[] bytes, ref int offset)
    {
        if (offset + 8 > bytes.Length)
        {
            offset = bytes.Length;
            return 0;
        }

        var value = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(offset, 8));
        offset += 8;
        return value;
    }

    static object ReadUInt64(byte[] bytes, ref int offset)
    {
        if (bytes.Length - offset < 8) throw new InvalidDataException("MessagePack uint64 数据不完整");
        var value = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset, 8));
        offset += 8;
        return value <= long.MaxValue ? (object)(long)value : value;
    }
}

