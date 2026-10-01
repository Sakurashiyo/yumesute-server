static class MagicOnionLz4
{
    const sbyte Lz4BlockArrayLengthCode = 98;

    public static object?[] Wrap(object? value)
    {
        var raw = MsgPack.Encode(value);
        return new object?[]
        {
            new MsgPack.Ext(Lz4BlockArrayLengthCode, MsgPack.Encode(raw.Length)),
            new MsgPack.Binary(CompressLiteralBlock(raw))
        };
    }

    public static object? Unwrap(object? value) => Unwrap(value, 1024 * 1024);

    public static object? Unwrap(object? value, int maxOutputBytes, Func<byte[], object?>? decode = null)
    {
        decode ??= MsgPack.Decode;
        if (value is MsgPack.Ext { Code: 99 } single)
        {
            if (single.Bytes.Length == 0) throw new InvalidDataException("缺少 LZ4 长度");
            var size = single.Bytes[0] switch
            {
                <= 0x7f => 1,
                0xcc => 2,
                0xcd => 3,
                0xce or 0xd2 => 5,
                _ => throw new InvalidDataException("LZ4 长度标记非法")
            };
            if (single.Bytes.Length <= size) throw new InvalidDataException("缺少 LZ4 数据");
            var length = ToInt(MsgPack.Decode(single.Bytes[..size]));
            if (length is null or <= 0 || length > maxOutputBytes) throw new InvalidDataException("LZ4 解压长度超限");
            return decode(DecompressBlock(single.Bytes[size..], length));
        }
        if (value is object?[] array)
        {
            if (TryUnwrapArray(array, maxOutputBytes, decode, out var unwrapped))
            {
                return unwrapped;
            }

            return array.Select(item => Unwrap(item, maxOutputBytes, decode)).ToArray();
        }

        return value;
    }

    public static object?[] UnwrapFrames(object?[] frames)
    {
        return frames.Select(frame => Unwrap(frame)).ToArray();
    }

    static bool TryUnwrapArray(object?[] array, int maxOutputBytes, Func<byte[], object?> decode, out object? value)
    {
        value = null;
        if (array.Length < 2 || array[0] is not MsgPack.Ext ext || ext.Code != Lz4BlockArrayLengthCode)
        {
            return false;
        }

        var lengths = ReadLengths(ext.Bytes);
        if (lengths.Length == 0)
        {
            return false;
        }

        if (array.Length - 1 != lengths.Length || array.Skip(1).Any(block => block is not byte[]))
            throw new InvalidDataException("LZ4 块数与长度标记不一致");
        if (lengths.Sum(n => (long)n) > maxOutputBytes) throw new InvalidDataException("LZ4 解压长度超限");
        var blocks = array.Skip(1).Cast<byte[]>().ToArray();

        using var ms = new MemoryStream();
        for (var i = 0; i < blocks.Length; i++)
        {
            var expected = i < lengths.Length ? lengths[i] : (int?)null;
            var block = DecompressBlock(blocks[i], expected);
            ms.Write(block);
        }

        value = decode(ms.ToArray());
        return true;
    }

    static int[] ReadLengths(byte[] bytes)
    {
        // 长度元数据只允许正整数或旧版的扁平整数数组，不能进入通用递归反序列化。
        if (bytes.Length == 0) throw new InvalidDataException("缺少 LZ4 长度");
        var offset = 0;
        int? count = null;
        if (bytes[0] is >= 0x90 and <= 0x9f) { count = bytes[0] & 15; offset = 1; }
        else if (bytes[0] == 0xdc)
        {
            if (bytes.Length < 3) throw new InvalidDataException("LZ4 长度元数据不完整");
            count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(1)); offset = 3;
        }
        else if (bytes[0] == 0xdd)
        {
            if (bytes.Length < 5) throw new InvalidDataException("LZ4 长度元数据不完整");
            count = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1))); offset = 5;
        }
        var values = new List<int>();
        while (offset < bytes.Length && (count is null || values.Count < count))
        {
            var size = bytes[offset] switch { <= 0x7f => 1, 0xcc or 0xd0 => 2, 0xcd or 0xd1 => 3,
                0xce or 0xd2 => 5, 0xcf or 0xd3 => 9, _ => throw new InvalidDataException("LZ4 长度必须为整数") };
            if (size > bytes.Length - offset) throw new InvalidDataException("LZ4 长度元数据不完整");
            var length = ToInt(MsgPack.Decode(bytes.AsSpan(offset, size).ToArray()));
            if (length is null or <= 0) throw new InvalidDataException("LZ4 块长度非法");
            values.Add(length.Value); offset += size;
        }
        if (offset != bytes.Length || values.Count == 0 || count is int expected && values.Count != expected)
            throw new InvalidDataException("LZ4 长度元数据不完整");
        return values.ToArray();
    }

    static int? ToInt(object? value)
    {
        return value switch
        {
            int i => i,
            long l when l <= int.MaxValue && l >= int.MinValue => (int)l,
            byte b => b,
            short s => s,
            ushort us => us,
            uint ui when ui <= int.MaxValue => (int)ui,
            _ => null
        };
    }

    internal static byte[] CompressLiteralBlock(byte[] raw)
    {
        using var ms = new MemoryStream();
        var length = raw.Length;
        if (length < 15)
        {
            ms.WriteByte((byte)(length << 4));
        }
        else
        {
            ms.WriteByte(0xf0);
            var remaining = length - 15;
            while (remaining >= 255)
            {
                ms.WriteByte(255);
                remaining -= 255;
            }
            ms.WriteByte((byte)remaining);
        }

        ms.Write(raw);
        return ms.ToArray();
    }

    static byte[] DecompressBlock(byte[] source, int? expectedLength)
    {
        var i = 0;
        var output = new List<byte>(expectedLength ?? source.Length * 2);
        while (i < source.Length)
        {
            var token = source[i++];
            var literalLength = token >> 4;
            if (literalLength == 15)
            {
                while (i < source.Length)
                {
                    var extra = source[i++];
                    literalLength += extra;
                    if (extra != 255) break;
                }
            }

            if (literalLength > source.Length - i) throw new InvalidDataException("LZ4 字面量数据不完整");
            var literalCount = literalLength;
            if ((long)output.Count + literalCount > expectedLength) throw new InvalidDataException("LZ4 字面量超过声明长度");
            output.AddRange(source.AsSpan(i, literalCount).ToArray());
            i += literalCount;
            if (i >= source.Length) break;

            if (i + 2 > source.Length) throw new InvalidDataException("LZ4 偏移数据不完整");
            var offset = source[i] | (source[i + 1] << 8);
            i += 2;
            if (offset == 0) throw new InvalidDataException("Invalid LZ4 offset 0.");

            var matchLength = (token & 0x0f) + 4;
            if ((token & 0x0f) == 15)
            {
                while (i < source.Length)
                {
                    var extra = source[i++];
                    matchLength += extra;
                    if (extra != 255) break;
                }
            }

            if ((long)output.Count + matchLength > expectedLength) throw new InvalidDataException("LZ4 匹配超过声明长度");
            var start = output.Count - offset;
            if (start < 0) throw new InvalidDataException("Invalid LZ4 back-reference.");
            for (var j = 0; j < matchLength; j++)
            {
                output.Add(output[start + j]);
            }
        }

        var result = output.ToArray();
        if (expectedLength.HasValue && result.Length != expectedLength.Value)
        {
            throw new InvalidDataException($"LZ4 length mismatch: got {result.Length}, expected {expectedLength.Value}.");
        }

        return result;
    }
}
