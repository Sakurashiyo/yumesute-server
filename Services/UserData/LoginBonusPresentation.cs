sealed partial class UserDataService
{
    // 按主页角色选择明确的立绘配置；SelectedSpineCostume 会把 3D 服装编号当成剧情立绘地址。
    static object?[] BuildRegularLoginBonusSpineGroup() => new object?[]
    {
        2,
        new object?[]
        {
            new object?[] { 101, null, null, "10101", "101512", 1, 200, null, 2, 2 },
            new object?[] { 102, null, null, "10201", "102512", null, 109, null, 3, 1 },
            new object?[] { 103, null, null, "10301", "103512", null, 109, null, 3, 1 },
            new object?[] { 104, null, null, "10401", "104512", null, 202, null, 13, 1 },
            new object?[] { 105, null, null, "10501", "105512", null, 200, null, 26, 2 },
            new object?[] { 106, null, null, "10601", "106512", null, 100, null, 25, 1 },
            new object?[] { 201, null, null, "20101", "201512", null, 109, null, 21, 1 },
            new object?[] { 202, null, null, "20201", "202512", 2, 74, null, 25, 4 },
            new object?[] { 203, null, null, "20301", "203512", null, 200, null, 2, 2 },
            new object?[] { 204, null, null, "20401", "204512", null, 100, null, 3, 1 },
            new object?[] { 205, null, null, "20501", "205512", null, 305, null, 17, 4 },
            new object?[] { 301, null, null, "30101", "301512", null, 202, null, 13, 1 },
            new object?[] { 302, null, null, "30201", "302512", 1, 109, null, 15, 1 },
            new object?[] { 303, null, null, "30301", "303512", 1, 109, null, 17, 1 },
            new object?[] { 304, null, null, "30401", "304512", null, 100, null, 16, 1 },
            new object?[] { 305, null, null, "30501", "305512", null, 200, null, 26, 2 },
            new object?[] { 401, null, null, "40101", "401512", 1, 109, null, 8, 2 },
            new object?[] { 402, null, null, "40201", "402513", null, 209, null, 2, 4 },
            new object?[] { 403, null, null, "40301", "403512", null, 200, null, 12, 1 },
            new object?[] { 404, null, null, "40401", "404512", null, 305, null, 3, 4 },
            new object?[] { 405, null, null, "40501", "405512", null, 109, null, 3, 1 },
            new object?[] { 901, null, null, "90102", "901512", 1, 100, null, 2, 1 },
            new object?[] { 902, null, null, "90202", "902512", null, 100, null, 25, 1 },
            new object?[] { 906, null, null, "90602", "906512", null, 200, null, 22, 2 },
            new object?[] { 909, null, null, "90902", "909512", null, 70, null, 23, 4 },
            new object?[] { 904, null, null, "90402", "904512", null, 100, null, 13, 1 },
        }
    };
}
