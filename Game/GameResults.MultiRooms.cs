static partial class GameResults
{
    public static object?[] MultiRoomsList()
    {
        var now = DateTime.UtcNow;
        return new object?[]
        {
            new object?[] { "97538137", "Local Room Extra", 0, 10, 128, now.AddHours(-1), now.AddDays(14), 203, 7303, 26503, false },
            new object?[] { "47031817", "Local Room Hard", 0, 2, 32, now.AddMinutes(-30), now.AddDays(11), 1703, 5303, 20903, false },
            new object?[] { "47609487", "Local Room Normal", 0, 17, 32, now.AddHours(-2), now.AddDays(10), 7603, 5303, 17903, false },
            new object?[] { "37820067", "Local Room", 0, 23, 32, now.AddHours(-3), now.AddDays(9), 5303, 21003, 4803, false },
            new object?[] { "47031017", "Local Solo", 0, 3, 32, now.AddHours(-4), now.AddDays(8), 23905, null, null, false }
        };
    }

    public static object?[] MultiRoomDetail()
    {
        var now = DateTime.UtcNow;
        return new object?[]
        {
            new object?[] { "97236627", "Local Multi Room", 0, 1, 128, now.AddHours(-1), now.AddDays(14), 5105, 12105, 5605, false },
            new object?[]
            {
                new object?[]
                {
                    "73110247",
                    1,
                    true,
                    null,
                    "302.5067",
                    1,
                    now,
                    new object?[] { "4152229175", "LocalPlayer", 202106, 2600614, 208028, 110010, 0, false, 190001 },
                    null,
                    new object?[] { 2862, 52, 13, 2, 0, 1 }
                }
            },
            false,
            null
        };
    }

    public static object?[] CircleSupportAndTheaterLevelInformation()
    {
        var timestamp = DateTime.UtcNow;
        // 主数据首次开放的上限是 11，初始上限因此为 10；零上限会使客户端误判为 MAX。
        return new object?[]
        {
            new object?[] { 0, 0, timestamp, 10, new object?[] { 0L, Array.Empty<object?>() } },
            new object?[] { 0, 0, timestamp, 10, new object?[] { 0L, Array.Empty<object?>() } },
            new object?[] { 0, 0, timestamp, 10, new object?[] { 0L, Array.Empty<object?>() } },
            new object?[] { 0, 0, timestamp, 10, new object?[] { 0L, Array.Empty<object?>() } }
        };
    }

    public static object?[] LessonCreatePartyResult()
    {
        return new object?[] { true };
    }

}
