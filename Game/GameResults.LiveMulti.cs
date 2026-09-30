static partial class GameResults
{
    public static object?[] MatchingGhostLiveResult()
    {
        return new object?[]
        {
            5,
            new object?[] { "8072561549", "GhostPlayer", 110205, null, null, 120010, 0, false, 190001, 1, 180001, null, null },
            new object?[]
            {
                1,
                new object?[]
                {
                    new object?[] { 1, 120010, 50, 5, 0, false, null, null, null, null, null, 2080, 2421, 2483, null },
                    new object?[] { 2, 120040, 50, 5, 0, false, null, null, null, null, null, 2038, 2126, 2481, null },
                    new object?[] { 3, 120050, 50, 5, 0, false, null, null, null, null, null, 2363, 2185, 2097, null },
                    new object?[] { 4, 120060, 50, 5, 0, false, null, null, null, null, null, 2208, 2392, 2300, null },
                    new object?[] { 5, 120030, 50, 5, 0, false, null, null, null, null, null, 2540, 2156, 1949, null }
                }
            }
        };
    }

    public static object?[] GhostLiveStartResult(object? requestBody = null)
    {
        LastLiveMasterId = ReadLiveMasterId(requestBody);
        return CapturedMultiStyleLiveStartResult(257192221, LastLiveMasterId);
    }

    public static object?[] MultiLiveStartResult(object? requestBody = null)
    {
        LastLiveMasterId = ReadFirstLiveMasterId(requestBody);
        return CapturedMultiStyleLiveStartResult(IsSecondMultiLive() ? 257193724 : 257192603, LastLiveMasterId);
    }

    public static object?[] GhostLiveStartPresentData(object? requestBody = null)
    {
        var liveMasterId = ReadLiveMasterId(requestBody);
        return LiveStartPresentDataFor(liveMasterId);
    }

    public static object?[] MultiLiveStartPresentData(object? requestBody = null)
    {
        var liveMasterId = ReadFirstLiveMasterId(requestBody);
        return LiveStartPresentDataFor(liveMasterId);
    }

    public static object?[] MultiLiveInformation(long multiLiveId)
    {
        if (multiLiveId == 11868654)
        {
            return new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["13"] = new object?[] { new object?[] { 40036472, "4152272985", 13 } },
                    ["39"] = new object?[] { new object?[] { 40036472, "4152272985", 39 }, new object?[] { 15737869, "1524742065", 39 }, new object?[] { 26390954, "5726833145", 39 } },
                    ["78"] = new object?[] { new object?[] { 40036472, "4152272985", 78 } },
                    ["104"] = new object?[] { new object?[] { 40036472, "4152272985", 104 }, new object?[] { 15737869, "1524742065", 104 } },
                    ["91"] = new object?[] { new object?[] { 26390954, "5726833145", 91 } }
                }
            };
        }

        return new object?[]
        {
            new Dictionary<string, object?>
            {
                ["39"] = new object?[] { new object?[] { 23024391, "5283719495", 39 } },
                ["65"] = new object?[] { new object?[] { 23024391, "5283719495", 65 } },
                ["104"] = new object?[] { new object?[] { 23024391, "5283719495", 104 } }
            }
        };
    }

    public static bool IsCompressedMultiLiveInformation(long multiLiveId)
    {
        return multiLiveId == 11868654;
    }

    public static object?[] GhostLiveFinishResult()
    {
        return new object?[]
        {
            null,
            Array.Empty<object?>(),
            new object?[] { new object?[] { 0.0, 98.7165 }, new object?[] { 0.0, 9.53 }, 11.77, 21.3 },
            null,
            null,
            1,
            4,
            null,
            false,
            0,
            0,
            Array.Empty<object?>(),
            Array.Empty<object?>(),
            95.2302,
            null,
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
            new object?[]
            {
                new object?[] { "8072561549", "GhostPlayer", 110205, null, null, 120010, 0, false, 190001, 1, 180001, null, null },
                892181,
                0,
                1,
                120010,
                false
            },
            null,
            Array.Empty<object?>()
        };
    }

    public static object?[] GhostLiveFinishStaticPresentData()
    {
        return new object?[]
        {
            DataObject(24, new object?[] { Random.Shared.Next(31800000, 31999999), 9701, 1, null, 98.71649932861328, 9.529999732971191, 1, 0, 4 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 24, null, 1600, 1602 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 21, null, 29, 29 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 24, null, 1800, 1801 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 5, null, 20, 20 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 2, 10105, 58, 7, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 104, 1, 10101, 24, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 103, 1, 10101, 24, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 1, 10101, 24, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 105, 1, 10101, 24, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 106, 1, 10101, 24, 5, 0, 0 })
        };
    }

    public static object?[] MultiLiveFinishResult()
    {
        if (IsSecondMultiLive())
        {
            return MultiLiveFinishResult17401();
        }

        return new object?[]
        {
            null,
            Array.Empty<object?>(),
            new object?[] { new object?[] { 0.0, 100.3957 }, new object?[] { 0.0, 9.68 }, 21.3, 30.98 },
            new object?[] { 20, 20, 295, 325, 30, 2087 },
            null,
            2,
            6,
            null,
            false,
            0,
            0,
            Array.Empty<object?>(),
            Array.Empty<object?>(),
            98.2042,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            new object?[] { 3482, null, null, null, null, null, null, 5659, 5631, 35013, null, null, null, null },
            new object?[]
            {
                new object?[] { new object?[] { 1, 130022, 15, null, null, null, false }, 1101, 0, false },
                new object?[] { new object?[] { 1, 130023, 22, null, null, null, false }, 1102, 0, false },
                new object?[] { new object?[] { 1, 120001, 7, null, null, null, false }, 1103, 0, false },
                new object?[] { new object?[] { 1, 130061, 7, null, null, null, false }, 1104, 0, false },
                new object?[] { new object?[] { 1, 130032, 30, null, null, null, false }, 1105, 0, false },
                new object?[] { new object?[] { 1, 130062, 22, null, null, null, false }, 1106, 0, false },
                new object?[] { new object?[] { 1, 130034, 15, null, null, null, false }, 1107, 0, false },
                new object?[] { new object?[] { 1, 130062, 7, null, null, null, false }, 1108, 0, false },
                new object?[] { new object?[] { 1, 120001, 7, null, null, null, false }, 1109, 0, false },
                new object?[] { new object?[] { 12, 0, 11148, null, null, null, false }, 0, 0, false }
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

    public static object?[] MultiLiveFinishStaticPresentData()
    {
        if (IsSecondMultiLive())
        {
            return MultiLiveFinishStaticPresentData17401();
        }

        return new object?[]
        {
            DataObject(24, new object?[] { Random.Shared.Next(31800000, 31999999), 7501, 1, null, 100.39569854736328, 9.680000305175781, 2, 0, 6 }),
            DataObject(101, new object?[] { Random.Shared.Next(3150000, 3169999), 35013, 149361, DateTime.UtcNow, null, true, 2 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 1, null, 41, 41 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 16, null, 42, 42 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 25, null, 1800, 1801 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 25, null, 1600, 1602 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 5395, null, 3200, 3201 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 5395, null, 39, 39 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 30, null, 29, 29 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 6, null, 20, 20 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 2, 10105, 60, 8, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 104, 1, 10101, 25, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 103, 1, 10101, 25, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 1, 10101, 25, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 105, 1, 10101, 25, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 106, 1, 10101, 25, 5, 0, 0 })
        };
    }

    static object?[] MultiLiveFinishResult17401()
    {
        return new object?[]
        {
            null,
            Array.Empty<object?>(),
            new object?[] { new object?[] { 0.0, 100.0291 }, new object?[] { 0.0, 8.58 }, 30.98, 39.56 },
            new object?[] { 20, 20, 325, 355, 30, 2057 },
            null,
            1,
            5,
            null,
            false,
            0,
            0,
            Array.Empty<object?>(),
            Array.Empty<object?>(),
            98.9124,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            new object?[] { 6204, null, null, null, null, null, null, 5642, 5602, 35013, null, null, null, null },
            new object?[]
            {
                new object?[] { new object?[] { 1, 130022, 22, null, null, null, false }, 1301, 0, false },
                new object?[] { new object?[] { 1, 130022, 15, null, null, null, false }, 1302, 0, false },
                new object?[] { new object?[] { 1, 120001, 7, null, null, null, false }, 1303, 0, false },
                new object?[] { new object?[] { 1, 130062, 7, null, null, null, false }, 1305, 0, false },
                new object?[] { new object?[] { 12, 0, 37448, null, null, null, false }, 0, 0, false }
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

    static object?[] MultiLiveFinishStaticPresentData17401()
    {
        return new object?[]
        {
            DataObject(24, new object?[] { Random.Shared.Next(31800000, 31999999), 17401, 1, null, 100.02909851074219, 8.579999923706055, 1, 0, 5 }),
            DataObject(147, new object?[] { 155, 0, 15 }),
            DataObject(101, new object?[] { Random.Shared.Next(3150000, 3169999), 35013, 155565, DateTime.UtcNow, null, true, 2 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 5425, null, 3200, 3201 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 5425, null, 39, 39 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 39, null, 29, 29 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 26, null, 1600, 1602 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 26, null, 1800, 1801 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 7, null, 20, 20 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 2, 10105, 62, 8, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 104, 1, 10101, 26, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 103, 1, 10101, 26, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 1, 10101, 26, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 105, 1, 10101, 26, 5, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 106, 1, 10101, 26, 5, 0, 0 })
        };
    }

    public static bool IsGhostLive()
    {
        return LastLiveMasterId is >= 9700 and < 9800;
    }

    public static bool IsMultiLive()
    {
        return LastLiveMasterId is >= 7500 and < 7600 || IsSecondMultiLive();
    }

    public static bool IsSecondMultiLive()
    {
        return LastLiveMasterId is >= 17400 and < 17500;
    }

    static object?[] LiveStartPresentDataFor(long liveMasterId, bool? isPossession = null)
    {
        // 歌曲关联以主数据为准，拒绝把无法识别的谱面转换成歌曲 0 下发到客户端缓存。
        if (!PlayerProgressionRules.Charts.TryGetValue(liveMasterId, out var chart) || chart.MusicMasterId <= 0)
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidStart);
        return new object?[]
        {
            DataObject(25, new object?[] { Random.Shared.Next(18000000, 18999999), null, chart.MusicMasterId, null, null, false, null, 0, 0, isPossession ?? liveMasterId < 17000 })
        };
    }

    static object?[] CapturedMultiStyleLiveStartResult(int seed, long liveMasterId)
    {
        return new object?[]
        {
            new Dictionary<string, object?>
            {
                ["1"] = new object?[] { 211035567, 104, 110040, 0, 0, 1, 1, new object?[] { 1153, 1135, 1060, 3348 }, new object?[] { 1153, 1135, 1060, 3348 }, false, null, 0, 1 },
                ["2"] = new object?[] { 211035566, 103, 110030, 0, 0, 2, 1, new object?[] { 1041, 1116, 1190, 3347 }, new object?[] { 1041, 1116, 1190, 3347 }, false, null, 0, 1 },
                ["3"] = new object?[] { 211035564, 101, 110010, 0, 0, 3, 1, new object?[] { 1171, 1265, 1023, 3459 }, new object?[] { 1171, 1265, 1023, 3459 }, false, null, 0, 1 },
                ["4"] = new object?[] { 211035568, 105, 110050, 0, 0, 4, 1, new object?[] { 1061, 1061, 1008, 3130 }, new object?[] { 1061, 1061, 1008, 3130 }, false, null, 0, 1 },
                ["5"] = new object?[] { 211035569, 106, 110060, 0, 0, 5, 1, new object?[] { 1078, 991, 1061, 3130 }, new object?[] { 1078, 991, 1061, 3130 }, false, null, 0, 1 }
            },
            MultiStyleSkillMap(liveMasterId),
            new object?[]
            {
                new object?[] { 21103556700L, 211035567, null, 30, 2, new object?[] { 2 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110040, 211035567 },
                new object?[] { 21103556600L, 211035566, null, 30, 3, new object?[] { 3 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110030, 211035566 },
                new object?[] { 21103556400L, 211035564, null, 30, 4, new object?[] { 4 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110010, 211035564 },
                new object?[] { 21103556800L, 211035568, null, 30, 3, new object?[] { 3 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110050, 211035568 },
                new object?[] { 21103556900L, 211035569, null, 30, 4, new object?[] { 4 }, Array.Empty<object?>(), new object?[] { 210, 0, 0, Array.Empty<object?>() }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), 110060, 211035569 }
            },
            Array.Empty<object?>(),
            new object?[] { 120, 211035564, 0, Array.Empty<object?>() },
            16414,
            3,
            1000,
            false,
            0,
            0.95,
            seed
        };
    }

    static Dictionary<string, object?> MultiStyleSkillMap(long liveMasterId)
    {
        if (liveMasterId is >= 17400 and < 17500)
        {
            return new Dictionary<string, object?>
            {
                ["13"] = new object?[] { new object?[] { 2 }, new object?[] { 2 }, false, false, new object?[] { 2 }, new object?[] { 21103556700L }, new object?[] { 10499911, 99910322 }, 0 },
                ["26"] = new object?[] { new object?[] { 2, 3 }, new object?[] { 3 }, false, false, new object?[] { 3 }, new object?[] { 21103556600L }, new object?[] { 10399911, 99910421 }, 0 },
                ["39"] = new object?[] { Array.Empty<object?>(), new object?[] { 4 }, true, false, new object?[] { 4 }, new object?[] { 21103556400L }, new object?[] { 10110411, 10110421 }, 0 },
                ["52"] = new object?[] { new object?[] { 3 }, new object?[] { 3 }, false, false, new object?[] { 3 }, new object?[] { 21103556800L }, new object?[] { 10510111, 10510121 }, 0 },
                ["65"] = new object?[] { new object?[] { 3, 4 }, new object?[] { 4 }, false, false, new object?[] { 4 }, new object?[] { 21103556900L }, new object?[] { 10699911, 99910421 }, 0 },
                ["78"] = new object?[] { new object?[] { 3, 4 }, Array.Empty<object?>(), false, false, new object?[] { 4 }, new object?[] { 21103556400L }, new object?[] { 10199912, 99910421 }, 0 },
                ["91"] = new object?[] { new object?[] { 3, 4 }, Array.Empty<object?>(), false, false, new object?[] { 3 }, new object?[] { 21103556600L }, new object?[] { 10399911, 99910521 }, 0 },
                ["104"] = new object?[] { Array.Empty<object?>(), new object?[] { 2 }, true, false, new object?[] { 2 }, new object?[] { 21103556700L }, new object?[] { 10410511, 10410521 }, 0 }
            };
        }

        return new Dictionary<string, object?>
        {
            ["13"] = new object?[] { new object?[] { 2 }, new object?[] { 2 }, false, false, new object?[] { 2 }, new object?[] { 21103556700L }, new object?[] { 10410311, 10410321 }, 0 },
            ["26"] = new object?[] { new object?[] { 2, 3 }, new object?[] { 3 }, false, false, new object?[] { 3 }, new object?[] { 21103556600L }, new object?[] { 10399911, 99910622 }, 0 },
            ["39"] = new object?[] { Array.Empty<object?>(), new object?[] { 4 }, true, false, new object?[] { 4 }, new object?[] { 21103556400L }, new object?[] { 10199912, 99910421 }, 0 },
            ["52"] = new object?[] { new object?[] { 3 }, new object?[] { 3 }, false, false, new object?[] { 3 }, new object?[] { 21103556800L }, new object?[] { 10599911, 99910322 }, 0 },
            ["65"] = new object?[] { new object?[] { 3, 4 }, new object?[] { 4 }, false, false, new object?[] { 4 }, new object?[] { 21103556900L }, new object?[] { 10610311, 10610321 }, 0 },
            ["78"] = new object?[] { new object?[] { 3, 4 }, Array.Empty<object?>(), false, false, new object?[] { 4 }, new object?[] { 21103556400L }, new object?[] { 10110311, 10110321 }, 0 },
            ["91"] = new object?[] { new object?[] { 3, 4 }, Array.Empty<object?>(), false, false, new object?[] { 3 }, new object?[] { 21103556600L }, new object?[] { 10399911, 99910622 }, 0 },
            ["104"] = new object?[] { Array.Empty<object?>(), new object?[] { 2 }, true, false, new object?[] { 2 }, new object?[] { 21103556700L }, new object?[] { 10499911, 99910521 }, 0 }
        };
    }

    static long ReadFirstLiveMasterId(object? requestBody)
    {
        return requestBody is object?[] values
            ? ConvertToLong(values.ElementAtOrDefault(0)) ?? 0
            : 0;
    }
}
