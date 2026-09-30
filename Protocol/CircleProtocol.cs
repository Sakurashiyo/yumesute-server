static class CircleProtocol
{
    // 与客户端 CircleResultStatus 共用编号，不能用 HTTP 状态代替业务状态。
    public const int RequestIllegal = 1;
    public const int AlreadyJoined = 4;
    public const int CreateSuccess = 14;
    public const int EditSuccess = 13;
    public const int DataNotFound = 19;
    public const int SearchSuccess = 20;
    public const int InvalidParameter = 25;
    public const string Unauthorized = "ACCOUNT_UNAUTHORIZED";

    public sealed record Payload(string Name, string Comment, int Start, int End, int Entry, int Member, long? Company, long? CharacterBase);

    public static Payload? ReadPayload(object? body)
    {
        if (body is not object?[] { Length: >= 9 } values
            || values[1] is not string name || string.IsNullOrWhiteSpace(name) || name.Length > 100
            || values[2] is not string comment || comment.Length > 1000
            || Integer(values[3]) is not long start || start is < -1 or > 29
            || Integer(values[4]) is not long end || end is < -1 or > 29
            || Integer(values[5]) is not long entry || entry is < 0 or > 2
            || Integer(values[6]) is not long member || member is < 0 or > 2
            || !OptionalId(values[7], out var company) || !OptionalId(values[8], out var characterBase))
            return null;
        return new(name.Trim(), comment, (int)start, (int)end, (int)entry, (int)member, company, characterBase);
    }

    public static long? Integer(object? value) => value switch
    {
        byte n => n, sbyte n => n, short n => n, ushort n => n,
        int n => n, uint n => n, long n => n, ulong n when n <= long.MaxValue => (long)n,
        _ => null
    };

    static bool OptionalId(object? value, out long? id)
    {
        id = Integer(value);
        return value is null || id is > 0;
    }

    public static object?[]? ReadBanner(object? body, out string? circleId)
    {
        circleId = null;
        if (body is not object?[] { Length: >= 9 } values || values[0] is not string id
            || string.IsNullOrWhiteSpace(id) || Integer(values[1]) is not long poster || poster < 0
            || Integer(values[6]) is not long rotation || rotation is < -360 or > 360 || values[7] is not bool display)
            return null;
        var coordinates = new double[5];
        foreach (var (index, slot) in new[] { (2, 0), (3, 1), (4, 2), (5, 3), (8, 4) })
        {
            if (values[index] is float f) coordinates[slot] = f;
            else if (values[index] is double d) coordinates[slot] = d;
            else if (Integer(values[index]) is long n) coordinates[slot] = n;
            else return null;
            if (!double.IsFinite(coordinates[slot]) || Math.Abs(coordinates[slot]) > float.MaxValue) return null;
        }
        if (coordinates[4] <= 0) return null;
        circleId = id;
        return new object?[] { poster, (float)coordinates[0], (float)coordinates[1], (float)coordinates[2],
            (float)coordinates[3], (int)rotation, display, (float)coordinates[4] };
    }
}
