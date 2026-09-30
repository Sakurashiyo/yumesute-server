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

    public static object? Unwrap(object? value)
    {
        if (value is object?[] array)
        {
            if (TryUnwrapArray(array, out var unwrapped))
            {
                return unwrapped;
            }

            return array.Select(Unwrap).ToArray();
        }

        return value;
    }

    public static object?[] UnwrapFrames(object?[] frames)
    {
        return frames.Select(Unwrap).ToArray();
    }

    static bool TryUnwrapArray(object?[] array, out object? value)
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
        var blocks = array.Skip(1).Cast<byte[]>().ToArray();

        using var ms = new MemoryStream();
        for (var i = 0; i < blocks.Length; i++)
        {
            var expected = i < lengths.Length ? lengths[i] : (int?)null;
            var block = DecompressBlock(blocks[i], expected);
            ms.Write(block);
        }

        value = MsgPack.Decode(ms.ToArray());
        return true;
    }

    static int[] ReadLengths(byte[] bytes)
    {
        // 官方多块请求将长度连续编码在 Ext 中，而非只编码第一个长度。
        var values = MsgPack.DecodeAll(bytes);
        if (values.Length == 1 && values[0] is object?[] legacy) values = legacy;
        return values.Select(value => ToInt(value) is int length && length > 0 ? length :
            throw new InvalidDataException("LZ4 块长度非法")).ToArray();
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

    static byte[] CompressLiteralBlock(byte[] raw)
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
