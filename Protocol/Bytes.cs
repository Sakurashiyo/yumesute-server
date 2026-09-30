static class Bytes
{
    public static byte[] Concat(params byte[][] chunks)
    {
        var total = chunks.Sum(chunk => chunk.Length);
        var output = new byte[total];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, output, offset, chunk.Length);
            offset += chunk.Length;
        }
        return output;
    }

    public static string ToHex(byte[] bytes, int maxLength)
    {
        var length = Math.Min(bytes.Length, maxLength);
        var chars = new char[length * 2];
        for (var i = 0; i < length; i++)
        {
            var value = bytes[i];
            chars[i * 2] = ToHexNibble(value >> 4);
            chars[i * 2 + 1] = ToHexNibble(value & 0x0f);
        }

        var suffix = bytes.Length > maxLength ? "..." : "";
        return new string(chars) + suffix;
    }

    static char ToHexNibble(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);
}

