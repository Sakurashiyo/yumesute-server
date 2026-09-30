static partial class GameResults
{
    public static object?[] LiveStartResult(object? requestBody = null)
    {
        var liveMasterId = ReadLiveMasterId(requestBody);
        LastLiveMasterId = liveMasterId;
        var startRatio = ReadLiveStartFlag(requestBody) == false ? 1.0 : 0.0;

        // TODO（Live）：后续需要根据玩家当前队伍、角色/卡牌状态、
        // 支援配置、技能定义，以及 Music/Live MasterData 生成开始演出数据，
        // 而不是继续使用下面这份抓包默认队伍模板。当前只有 live master id
        // 的派生字段，以及请求标志反映到 startRatio 的部分是动态的。
        return new object?[]
        {
            new Dictionary<int, object?>
            {
                [1] = new object?[] { 211035564, 101, 110010, 0, 0, 1, 1, new object?[] { 16, 17, 14, 47 }, new object?[] { 16, 17, 14, 47 }, false, null, 0, 1 },
                [2] = new object?[] { 211035566, 103, 110030, 0, 0, 2, 1, new object?[] { 15, 16, 17, 48 }, new object?[] { 15, 16, 17, 48 }, false, null, 0, 1 },
                [3] = new object?[] { 211035567, 104, 110040, 0, 0, 3, 1, new object?[] { 16, 16, 15, 47 }, new object?[] { 16, 16, 15, 47 }, false, null, 0, 1 },
                [4] = new object?[] { 211035568, 105, 110050, 0, 0, 4, 1, new object?[] { 16, 16, 15, 47 }, new object?[] { 16, 16, 15, 47 }, false, null, 0, 1 },
                [5] = new object?[] { 211035569, 106, 110060, 0, 0, 5, 1, new object?[] { 16, 15, 16, 47 }, new object?[] { 16, 15, 16, 47 }, false, null, 0, 1 }
            },
            new Dictionary<int, object?>
            {
                [11] = new object?[] { new object?[] { 4 }, new object?[] { 4 }, false, false, new object?[] { 4 }, new object?[] { 21103556400L }, new object?[] { 10199912, 99910522 }, 0 },
                [22] = new object?[] { new object?[] { 4, 3 }, new object?[] { 3 }, false, false, new object?[] { 3 }, new object?[] { 21103556600L }, new object?[] { 10310411, 10310421 }, 0 },
                [33] = new object?[] { Array.Empty<object?>(), new object?[] { 2 }, true, false, new object?[] { 2 }, new object?[] { 21103556700L }, new object?[] { 10499911, 99910622 }, 0 },
                [44] = new object?[] { new object?[] { 3 }, new object?[] { 3 }, false, false, new object?[] { 3 }, new object?[] { 21103556800L }, new object?[] { 10510411, 10510421 }, 0 },
                [55] = new object?[] { new object?[] { 3, 4 }, new object?[] { 4 }, false, false, new object?[] { 4 }, new object?[] { 21103556900L }, new object?[] { 10699911, 99910322 }, 0 },
                [66] = new object?[] { Array.Empty<object?>(), new object?[] { 2 }, true, false, new object?[] { 2 }, new object?[] { 21103556700L }, new object?[] { 10499911, 99910621 }, 0 },
                [77] = new object?[] { new object?[] { 3 }, new object?[] { 3 }, false, false, new object?[] { 3 }, new object?[] { 21103556600L }, new object?[] { 10310511, 10310521 }, 0 },
                [88] = new object?[] { new object?[] { 3, 4 }, new object?[] { 4 }, false, false, new object?[] { 4 }, new object?[] { 21103556400L }, new object?[] { 10199911, 99910422 }, 0 }
            },
            new object?[]
            {
                new object?[] { 21103556400L, 211035564, null, 30, 4, new object?[] { 4 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110010, 211035564 },
                new object?[] { 21103556600L, 211035566, null, 30, 3, new object?[] { 3 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110030, 211035566 },
                new object?[] { 21103556700L, 211035567, null, 30, 2, new object?[] { 2 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110040, 211035567 },
                new object?[] { 21103556800L, 211035568, null, 30, 3, new object?[] { 3 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110050, 211035568 },
                new object?[] { 21103556900L, 211035569, null, 30, 4, new object?[] { 4 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110060, 211035569 }
            },
            Array.Empty<object?>(),
            new object?[] { 120, 211035564, 0, Array.Empty<object?>() },
            236,
            3,
            1000,
            false,
            0,
            startRatio,
            Random.Shared.Next(100000000, 999999999)
        };
    }

    public static object?[] LiveStartPresentData(object? requestBody = null)
    {
        return LiveStartPresentDataFor(ReadLiveMasterId(requestBody), true);
    }

    public static object?[] LiveStartLessonResult(object? requestBody = null)
    {
        // TODO（Live）：教程/课程演出的开始数据后续应根据 lesson 队伍状态、
        // lesson master data 和选中的 lesson live id 生成。当前先复刻抓包中
        // 第一次课程的单角色队伍结构。
        return new object?[]
        {
            new Dictionary<int, object?>
            {
                [1] = new object?[] { 211035564, 101, 110010, 0, 0, 1, 1, new object?[] { 15, 16, 13, 44 }, new object?[] { 15, 16, 13, 44 }, false, null, 0, 1 }
            },
            new Dictionary<int, object?>
            {
                [17] = new object?[] { Array.Empty<object?>(), Array.Empty<object?>(), false, false, Array.Empty<object?>(), new object?[] { 21103556400L }, new object?[] { 10199912 }, 0 },
                [34] = new object?[] { Array.Empty<object?>(), Array.Empty<object?>(), false, false, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 0 },
                [51] = new object?[] { Array.Empty<object?>(), Array.Empty<object?>(), false, false, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 0 },
                [68] = new object?[] { Array.Empty<object?>(), Array.Empty<object?>(), false, false, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 0 },
                [85] = new object?[] { Array.Empty<object?>(), Array.Empty<object?>(), false, false, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 0 }
            },
            new object?[]
            {
                new object?[] { 21103556400L, 211035564, null, 30, 4, new object?[] { 4 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110010, 211035564 }
            },
            Array.Empty<object?>(),
            new object?[] { 0, 0, 0, Array.Empty<object?>() },
            44,
            0,
            0,
            false,
            0,
            1.0,
            0
        };
    }

    public static object?[] LiveFinishResult()
    {
        if (LastLiveMasterId == 101)
        {
            return LiveFinishResult101();
        }

        if (IsTripleCastLive())
        {
            return TripleCastLiveFinishResult();
        }

        if (IsLeagueLive())
        {
            return LeagueLiveFinishResult();
        }

        if (IsGhostLive())
        {
            return GhostLiveFinishResult();
        }

        if (IsMultiLive())
        {
            return MultiLiveFinishResult();
        }

        // TODO（Live）：后续需要从 FinishAndValidate 解出来的成绩包计算结算结果。
        // 这里应包含分数/评级变化、连击/通关状态、玩家经验/等级进度、
        // 活动点数、道具/金币奖励，以及通关/任务通知。当前结构先复刻
        // 官方抓包，作为兼容占位。
        return new object?[]
        {
            null,
            Array.Empty<object?>(),
            null,
            new object?[] { 1, 4, 0, 39, 189, 11 },
            null,
            0,
            9,
            null,
            false,
            0,
            0,
            Array.Empty<object?>(),
            Array.Empty<object?>(),
            90.5185,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            new object?[] { 5715, null, null, null, null, null, null, 8451, 8009, 35013, null, null, null, null },
            new object?[]
            {
                new object?[] { new object?[] { 1, 130021, 105, null, null, null, false }, 101, 0, false },
                new object?[] { new object?[] { 1, 130023, 35, null, null, null, false }, 102, 0, false },
                new object?[] { new object?[] { 1, 120001, 35, null, null, null, false }, 103, 0, false },
                new object?[] { new object?[] { 12, 0, 3510, null, null, null, false }, 0, 0, false }
            },
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<object?>()
        };
    }

    public static object?[] LiveFinishStaticPresentData()
    {
        if (LastLiveMasterId == 101)
        {
            return LiveFinishPresentData101();
        }

        if (IsTripleCastLive())
        {
            return TripleCastLiveFinishStaticPresentData();
        }

        if (IsLeagueLive())
        {
            return LeagueLiveFinishStaticPresentData();
        }

        if (IsGhostLive())
        {
            return GhostLiveFinishStaticPresentData();
        }

        if (IsMultiLive())
        {
            return MultiLiveFinishStaticPresentData();
        }

        // TODO（Live）：PresentData 后续必须由和 LiveFinishResult 相同的演出
        // 结算计算器生成，再和已持久化的玩家、货币、道具等行合并。
        // 当前这个临时块先复刻一份 FinishAndValidate 官方抓包返回。
        return new object?[]
        {
            new object?[] { 24, new object?[] { Random.Shared.Next(30000000, 39999999), 7703, 1, null, 0.0, 0.0, 0, 0, 0 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 432607, 189 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130023, 35 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130021, 105 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 120001, 35 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130011, 60 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 4235013, 5715 } },
            new object?[] { 109, new object?[] { Random.Shared.Next(5000000, 5999999), 1, 0, DateTime.UtcNow, 0 } },
            new object?[] { 101, new object?[] { Random.Shared.Next(3000000, 3999999), 35013, 5715, DateTime.UtcNow, null, false, 1 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(120000000, 129999999), false, false, 189, null, 3200, 3201 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(120000000, 129999999), false, false, 189, null, 39, 39 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(120000000, 129999999), false, false, 189, null, 5, 5 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(120000000, 129999999), false, false, 3510, null, 2800, 2801 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(120000000, 129999999), true, false, 2, null, 22, 22 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 2, 10105, 2, 1, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 1, 10101, 1, 1, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 103, 1, 10101, 1, 1, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 104, 1, 10101, 1, 1, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 105, 1, 10101, 1, 1, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 106, 1, 10101, 1, 1, 0, 0 } }
        };
    }

    public static object?[] LiveFinishNotifications()
    {
        if (LastLiveMasterId == 101)
        {
            return new object?[]
            {
                new object?[] { 0, new object?[] { 4, 4 } }
            };
        }

        if (IsTripleCastLive() || IsLeagueLive())
        {
            return Array.Empty<object?>();
        }

        if (IsGhostLive())
        {
            return Array.Empty<object?>();
        }

        if (IsMultiLive())
        {
            if (IsSecondMultiLive())
            {
                return Array.Empty<object?>();
            }

            return new object?[]
            {
                new object?[] { 0, new object?[] { 41, 41 } }
            };
        }

        return new object?[]
        {
            new object?[] { 0, new object?[] { 22, 22 } }
        };
    }

    public static bool IsLeagueLive()
    {
        return LastLiveMasterId is >= 1001900 and < 1002000;
    }

    public static bool IsTripleCastLive()
    {
        return LastLiveMasterId is >= 1004500 and < 1004600;
    }

    static object?[] LiveFinishResult101()
    {
        return new object?[]
        {
            null,
            Array.Empty<object?>(),
            new object?[] { new object?[] { 0.0, 91.9673 }, new object?[] { 0.0, 4.18 }, 0.0, 4.18 },
            null,
            null,
            1,
            2,
            null,
            false,
            0,
            1,
            new object?[]
            {
                new object?[] { 1, 130011, 50, null, null, null, false },
                new object?[] { 1, 144001, 50, null, null, null, false },
                new object?[] { 1, 110002, 1, null, null, null, false },
                new object?[] { 1, 130041, 100, null, null, null, false },
                new object?[] { 1, 130042, 50, null, null, null, false }
            },
            Array.Empty<object?>(),
            99.78,
            1,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            null,
            Array.Empty<object?>(),
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<object?>()
        };
    }

    static object?[] LiveFinishPresentData101()
    {
        return new object?[]
        {
            new object?[] { 24, new object?[] { Random.Shared.Next(30000000, 39999999), 101, 1, null, 91.96730041503906, 4.179999828338623, 1, 0, 2 } },
            new object?[] { 128, new object?[] { Random.Shared.Next(5000000, 5999999), 221311, 5960, 0 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 120002, 780 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 144001, 50 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 110002, 1 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130011, 190 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130041, 200 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130042, 150 } },
            new object?[] { 90, new object?[] { Random.Shared.Next(6000000, 6999999), 1, 1, 0, null, 0 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 1, null, 20, 20 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 1, null, 40, 40 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 1, null, 4, 4 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 4, null, 29, 29 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 3, null, 1800, 1801 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 3, null, 3, 3 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 3, null, 1600, 1601 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 4, null, 42, 42 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 3, null, 6, 6 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 399, null, 3600, 3601 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 8, null, 3400, 3401 } },
            new object?[] { 48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 8, null, 22, 22 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 2, 10105, 8, 3, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 104, 1, 10101, 3, 1, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 103, 1, 10101, 3, 1, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 1, 10101, 3, 1, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 105, 1, 10101, 3, 1, 0, 0 } },
            new object?[] { 96, new object?[] { Random.Shared.Next(260000000, 269999999), 106, 1, 10101, 3, 1, 0, 0 } }
        };
    }
}
