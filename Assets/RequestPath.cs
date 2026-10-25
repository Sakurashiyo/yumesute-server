static class RequestPath
{
    // 路由参数在框架层已经解码一次；这里只做一次容错解码并统一分隔符。
    // 二次解码会把 %252e%252e%252f 变回 ../，因此必须先标记再解一次，遇到非法转义按不存在处理。
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (decoded.Contains('\0')) return null;
        return decoded.Replace('\\', '/');
    }
}
