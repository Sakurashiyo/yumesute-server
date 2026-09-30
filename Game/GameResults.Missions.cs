static partial class GameResults
{
    public static object?[] CharacterBulkLevelUpPresentData(object? payload)
    {
        // TODO（角色）：后续需要根据角色经验表和客户端请求的目标等级，
        // 计算升级后的等级、剩余经验、消耗道具、参数资源和任务进度。
        // 当前先复刻抓包中初始 5 人队伍的批量升级返回，保证流程可跑通。
        return new object?[]
        {
            new object?[] { 4, new object?[] { 211035564, 110010, 35, 6, 0, 0, 115004672, 1, 0, 0, false, null, 0, 1, false } },
            new object?[] { 4, new object?[] { 211035566, 110030, 35, 1552, 0, 0, 115004674, 1, 0, 0, false, null, 0, 1, false } },
            new object?[] { 4, new object?[] { 211035567, 110040, 36, 51, 0, 0, 115004675, 1, 0, 0, false, null, 0, 1, false } },
            new object?[] { 4, new object?[] { 211035568, 110050, 35, 143, 0, 0, 115004676, 1, 0, 0, false, null, 0, 1, false } },
            new object?[] { 4, new object?[] { 211035569, 110060, 35, 98, 0, 0, 115004677, 1, 0, 0, false, null, 0, 1, false } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 120001, 0 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 120002, 0 } },
            new object?[] { 67, new object?[] { Random.Shared.Next(5000000, 5999999), 79.5199966430664, 0.0 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 1, null, 12, 12 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 5, null, 8, 8 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 192, null, 1200, 1201 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 177, null, 120, 121 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 5, null, 42, 42 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 106, 3, 10201, 35, 6, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 105, 3, 10201, 35, 6, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 104, 3, 10201, 36, 6, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 103, 3, 10201, 35, 6, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 3, 10201, 35, 6, 0, 0 } }
        };
    }

    public static object?[] CharacterBulkLevelUpNotifications()
    {
        return new object?[]
        {
            new object?[] { 0, new object?[] { 8, 8 } }
        };
    }

    public static object?[] MissionRewardResult()
    {
        return new object?[]
        {
            new object?[] { 1, 130041, 500, null, null, null, false }
        };
    }

    public static object? MissionReceiveRewardsResult(long? missionId, int? missionCategory)
    {
        if (missionCategory == 4)
        {
            return new object?[]
            {
                new object?[] { 1, 130014, 1, null, null, null, false }
            };
        }

        return missionId switch
        {
            1000 => new object?[]
            {
                new object?[] { 13, 0, 100, null, null, null, false }
            },
            100000 => new object?[]
            {
                new object?[] { 1, 130014, 1, null, null, null, false }
            },
            1600 => new object?[]
            {
                new object?[] { 13, 0, 50, null, null, null, false }
            },
            200000 => new object?[]
            {
                new object?[] { 1, 110002, 2, null, null, null, false }
            },
            200200 => new object?[]
            {
                new object?[] { 1, 120002, 200, null, null, null, false }
            },
            _ => MissionRewardResult()
        };
    }

    public static object?[] MissionReceiveRewardsPresentData(long? missionId, int? missionCategory)
    {
        if (missionCategory == 4)
        {
            return new object?[]
            {
                new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, true, 1, null, 100200, 100201 } },
                new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130014, 2 } }
            };
        }

        return missionId switch
        {
            1000 => new object?[]
            {
                new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 11, null, 1000, 1002 } },
                new object?[] { 128, new object?[] { Random.Shared.Next(5000000, 5999999), 221311, 5930, 0 } }
            },
            100000 => new object?[]
            {
                new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130014, 1 } },
                new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, true, 709, null, 100000, 100001 } }
            },
            1600 => new object?[]
            {
                new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 17, null, 1600, 1602 } },
                new object?[] { 128, new object?[] { Random.Shared.Next(5000000, 5999999), 221311, 7570, 0 } }
            },
            200000 => new object?[]
            {
                new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, true, 4165, null, 200000, 200001 } },
                new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 110002, 3 } }
            },
            200200 => new object?[]
            {
                new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, true, 6, null, 200200, 200201 } },
                new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 120002, 750 } }
            },
            _ => Array.Empty<object?>()
        };
    }

    public static object?[] MissionPassReceiveRewardsResult(long missionPassId)
    {
        return new object?[]
        {
            new object?[]
            {
                new object?[] { 1, 130021, 500, null, null, null, false }
            },
            5
        };
    }

    public static object?[] MissionPassReceiveRewardsPresentData(long missionPassId)
    {
        return new object?[]
        {
            new object?[] { 97, new object?[] { Random.Shared.Next(8000000, 8999999), 0, missionPassId, false, 1, 0, false, 1, 0, 1, 0 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130021, 722 } }
        };
    }

}
