static partial class GameResults
{
    public static object?[] LeagueListResult()
    {
        return new object?[] { Array.Empty<object?>(), 0, Array.Empty<object?>(), Array.Empty<object?>(), true, Array.Empty<object?>() };
    }

    public static object?[] LeagueListPresentData()
    {
        return new object?[]
        {
            DataObject(107, new object?[] { 156, 1, 0, true, null, false, 2, null, 0, 0, 0 }),
            DataObject(100, new object?[] { 0, 0, 0, 1, 0, 3 })
        };
    }

    public static object?[] LeagueTopMenuInformation()
    {
        return new object?[]
        {
            LocalDate(2026, 7, 21, 13, 0, 0),
            LocalDate(2026, 7, 27, 8, 0, 0),
            LocalDate(2026, 7, 28, 13, 0, 0),
            10019,
            null,
            null,
            0,
            null,
            0,
            0,
            null,
            null,
            10,
            false,
            27
        };
    }

    public static object?[] LeagueTopMenuPresentData(object?[] user, object?[] profile)
    {
        return new object?[]
        {
            DataObject(100, new object?[] { 0, 0, 0, 1, 0, null }),
            DataObject(0, user),
            DataObject(1, profile),
            DataObject(106, new object?[] { 29611, 157, null })
        };
    }

    public static object?[] LeagueGroupRankingResult(long leagueMasterId, string publicId, string displayName)
    {
        return new object?[]
        {
            BuildLeagueRankingRow(1, "5242768385", 2624578426, leagueMasterId, "Ranker01", highPower: true),
            BuildLeagueRankingRow(2, "5386363265", 308886712, leagueMasterId, "Ranker02", highPower: true),
            BuildLeagueRankingRow(3, publicId, 514516, leagueMasterId, string.IsNullOrWhiteSpace(displayName) ? "LocalPlayer" : displayName, highPower: false),
            BuildLeagueRankingRow(4, "5222306219", 299665, leagueMasterId, "Ranker04", highPower: false),
            new object?[] { null, "5444923182", 0, leagueMasterId, 1, "Snow0111", 0, Array.Empty<object?>(), 0 }
        };
    }

    public static object?[] TripleCastListResult()
    {
        return new object?[] { Array.Empty<object?>(), 0, Array.Empty<object?>(), Array.Empty<object?>(), true, Array.Empty<object?>() };
    }

    public static object?[] TripleCastListPresentData()
    {
        return new object?[]
        {
            DataObject(176, new object?[] { 3032, 1, 0, true, null, false, 2, null, 0, 0, 0 }),
            DataObject(168, new object?[] { 253256, 0, 0, 1, 0, 2, null, null, null })
        };
    }

    public static object?[] TripleCastTopMenuInformation()
    {
        return new object?[]
        {
            LocalDate(2026, 7, 15, 13, 0, 0),
            LocalDate(2026, 7, 28, 8, 0, 0),
            LocalDate(2026, 8, 1, 13, 0, 0),
            10045,
            null,
            null,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            39
        };
    }

    public static object?[] TripleCastTopMenuPresentData(object?[] user, object?[] profile)
    {
        return new object?[]
        {
            DataObject(168, new object?[] { 253256, 0, 0, 1, 0, null, null, null, null }),
            DataObject(0, user),
            DataObject(1, profile),
            DataObject(170, new object?[] { 3139, 3033, null })
        };
    }

    public static object?[] TripleCastLiveStartResult(object? requestBody = null)
    {
        var result = new object?[]
        {
            LiveStartResult(requestBody),
            LiveStartResult(requestBody),
            LiveStartResult(requestBody)
        };

        LastLiveMasterId = ReadTripleCastLiveMasterId(requestBody);
        return result;
    }

    public static object?[] TripleCastLiveStartPresentData(object? requestBody = null)
    {
        return new object?[]
        {
            LiveStartPresentDataFor(ReadTripleCastLiveMasterId(requestBody), false)[0],
            DataObject(168, new object?[] { 253256, 0, 0, 1, 0, 2, 0, 1, 2 })
        };
    }

    public static object?[] LeagueLiveFinishResult()
    {
        return new object?[]
        {
            null,
            new object?[]
            {
                new object?[] { 1, 130041, 50, null, null, null, false },
                new object?[] { 1, 130042, 50, null, null, null, false },
                new object?[] { 1, 130043, 50, null, null, null, false }
            },
            new object?[] { new object?[] { 0.0, 99.72750091552734 }, new object?[] { 0.0, 0.0 }, 11.77, 11.77 },
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
            95.8144,
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
            new object?[] { null, 11, 0, null, 27, false },
            null,
            null,
            null,
            null,
            Array.Empty<object?>()
        };
    }

    public static object?[] LeagueLiveFinishStaticPresentData()
    {
        var partyMembers = LocalLeaguePartyMembers();

        return new object?[]
        {
            DataObject(24, new object?[] { Random.Shared.Next(31800000, 31999999), 1001901, 1, null, 99.72750091552734, 0.0, 1, 0, 4 }),
            DataObject(106, new object?[] { 29611, 157, 514516 }),
            DataObject(112, partyMembers[0]),
            DataObject(112, partyMembers[1]),
            DataObject(112, partyMembers[2]),
            DataObject(112, partyMembers[3]),
            DataObject(112, partyMembers[4]),
            DataObject(111, new object?[] { 2146342, 157, 514516, 0, 1, 0, 0, partyMembers, 0, 1, 1 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 11, null, 27, 27 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 1, null, 1800, 1801 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 1, null, 1600, 1602 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 1, null, 20, 20 })
        };
    }

    public static object?[] TripleCastLiveFinishResult()
    {
        return new object?[]
        {
            null,
            new object?[]
            {
                new object?[] { 1, 130041, 150, null, null, null, false },
                new object?[] { 1, 130042, 150, null, null, null, false },
                new object?[] { 1, 130043, 150, null, null, null, false }
            },
            new object?[] { new object?[] { 0.0, 99.62169647216797 }, new object?[] { 0.0, 0.0 }, 11.77, 11.77 },
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
            0.0,
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
            new object?[] { null, 5, 0, null, 39, false },
            null,
            null,
            null,
            null,
            Array.Empty<object?>()
        };
    }

    public static object?[] TripleCastLiveFinishStaticPresentData()
    {
        var group1 = TripleCastPartyMembers(478585, new object?[]
        {
            TripleCastPartyMember(47858501, 1, 110010, 44, new object?[] { 1534, 1265, 1171 }),
            TripleCastPartyMember(47858502, 2, 110030, 44, new object?[] { 1785, 1116, 1041 }),
            TripleCastPartyMember(47858503, 3, 110040, 44, new object?[] { 1590, 1135, 1153 }),
            TripleCastPartyMember(47858504, 4, 110050, 43, new object?[] { 1512, 1061, 1061 }),
            TripleCastPartyMember(47858505, 5, 110060, 43, new object?[] { 1591, 991, 1078 })
        });
        var group2 = TripleCastPartyMembers(478586, new object?[]
        {
            TripleCastPartyMember(47858601, 1, 110070, 1, new object?[] { 22, 30, 27 }),
            TripleCastPartyMember(47858602, 2, 110080, 1, new object?[] { 22, 45, 18 }),
            TripleCastPartyMember(47858603, 3, 110090, 1, new object?[] { 19, 36, 27 }),
            TripleCastPartyMember(47858604, 4, 110100, 1, new object?[] { 24, 36, 22 }),
            TripleCastPartyMember(47858605, 5, 110110, 1, new object?[] { 24, 30, 25 })
        });
        var group3 = TripleCastPartyMembers(478587, new object?[]
        {
            TripleCastPartyMember(47858701, 1, 110120, 1, new object?[] { 14, 17, 24 }),
            TripleCastPartyMember(47858702, 2, 110130, 1, new object?[] { 17, 14, 24 }),
            TripleCastPartyMember(47858703, 3, 110140, 1, new object?[] { 19, 16, 17 }),
            TripleCastPartyMember(47858704, 4, 110150, 1, new object?[] { 13, 16, 27 }),
            TripleCastPartyMember(47858705, 5, 110160, 1, new object?[] { 17, 14, 23 })
        });

        return new object?[]
        {
            DataObject(24, new object?[] { Random.Shared.Next(31800000, 31999999), 1004501, 1, null, 99.62169647216797, 0.0, 1, 0, 4 }),
            DataObject(170, new object?[] { 3139, 3033, 470021 }),
            DataObject(172, group1[0]),
            DataObject(172, group1[1]),
            DataObject(172, group1[2]),
            DataObject(172, group1[3]),
            DataObject(172, group1[4]),
            DataObject(172, group2[0]),
            DataObject(172, group2[1]),
            DataObject(172, group2[2]),
            DataObject(172, group2[3]),
            DataObject(172, group2[4]),
            DataObject(172, group3[0]),
            DataObject(172, group3[1]),
            DataObject(172, group3[2]),
            DataObject(172, group3[3]),
            DataObject(172, group3[4]),
            DataObject(171, new object?[] { 478585, 3033, 0, 455921, group1, 0, 1, 1 }),
            DataObject(171, new object?[] { 478586, 3033, 1, 8922, group2, 0, 1, 1 }),
            DataObject(171, new object?[] { 478587, 3033, 2, 5178, group3, 0, 1, 1 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 23, null, 1800, 1801 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 23, null, 1600, 1602 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 4, null, 20, 20 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 1870, null, 3600, 3601 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 59, null, 3400, 3401 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 59, null, 22, 22 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 305, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 304, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 303, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 302, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 301, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 301, 2, 10105, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 205, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 204, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 203, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 202, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 201, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 201, 2, 10105, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 2, 10105, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 101, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 103, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 104, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 105, 1, 10101, 1, 1, 0, 0 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 269999999), 106, 1, 10101, 1, 1, 0, 0 })
        };
    }

    static object?[] BuildLeagueRankingRow(int rank, string publicId, long score, long leagueMasterId, string displayName, bool highPower)
    {
        return new object?[]
        {
            rank,
            publicId,
            score,
            leagueMasterId,
            1,
            displayName,
            0,
            highPower ? HighPowerLeagueDeck() : LocalLeagueDeck(),
            highPower ? 3 : 1
        };
    }

    static object?[] HighPowerLeagueDeck()
    {
        return new object?[]
        {
            new object?[] { 1, 140760, 0, null, 0, null, 220, 230340, null, 10, 4, 330100, null, 10, new object?[] { 247269, 536646, 176586 }, 0, 5, 1, false },
            new object?[] { 2, 140410, 0, null, 0, null, 220, 230520, null, 10, 4, 330110, null, 10, new object?[] { 189804, 681039, 177312 }, 0, 5, 1, true },
            new object?[] { 3, 140920, 0, null, 0, null, 220, 230210, null, 10, 4, 332090, null, 10, new object?[] { 184296, 783921, 177420 }, 0, 5, 1, true },
            new object?[] { 4, 140290, 0, null, 0, null, 220, 230100, null, 10, 4, 332160, null, 10, new object?[] { 158766, 569292, 268251 }, 0, 5, 1, true },
            new object?[] { 5, 140770, 0, null, 0, null, 220, 230240, null, 10, 4, 330090, null, 10, new object?[] { 205401, 423945, 196464 }, 0, 5, 1, true }
        };
    }

    static object?[] LocalLeagueDeck()
    {
        return new object?[]
        {
            new object?[] { 1, 110040, 0, null, 0, null, 44, null, null, null, null, null, null, null, new object?[] { 1060, 1135, 1153 }, 0, 0, 0, false },
            new object?[] { 2, 110030, 0, null, 0, null, 44, null, null, null, null, null, null, null, new object?[] { 1190, 1116, 1041 }, 0, 0, 0, false },
            new object?[] { 3, 110010, 0, null, 0, null, 44, null, null, null, null, null, null, null, new object?[] { 1023, 1265, 1171 }, 0, 0, 0, false },
            new object?[] { 4, 110050, 0, null, 0, null, 43, null, null, null, null, null, null, null, new object?[] { 1008, 1061, 1061 }, 0, 0, 0, false },
            new object?[] { 5, 110060, 0, null, 0, null, 43, null, null, null, null, null, null, null, new object?[] { 1591, 1486, 1617 }, 0, 0, 0, false }
        };
    }

    static object?[][] LocalLeaguePartyMembers()
    {
        return new[]
        {
            new object?[] { 10371706, 2146342, 1, 110040, 44, null, null, null, null, null, null, null, new object?[] { 1060, 1135, 1153 }, 0, 0, false },
            new object?[] { 10371707, 2146342, 2, 110030, 44, null, null, null, null, null, null, null, new object?[] { 1190, 1116, 1041 }, 0, 0, false },
            new object?[] { 10371708, 2146342, 3, 110010, 44, null, null, null, null, null, null, null, new object?[] { 1023, 1265, 1171 }, 0, 0, false },
            new object?[] { 10371709, 2146342, 4, 110050, 43, null, null, null, null, null, null, null, new object?[] { 1008, 1061, 1061 }, 0, 0, false },
            new object?[] { 10371710, 2146342, 5, 110060, 43, null, null, null, null, null, null, null, new object?[] { 1591, 1486, 1617 }, 0, 0, false }
        };
    }

    static object?[] TripleCastPartyMember(long rowId, int order, long characterMasterId, int level, object?[] stats)
    {
        return new object?[] { rowId, order, 3, 10201, characterMasterId, level, null, null, null, null, null, null, stats, 0, 0, false };
    }

    static object?[][] TripleCastPartyMembers(long partyId, object?[] members)
    {
        var rows = new object?[members.Length][];
        for (var i = 0; i < members.Length; i++)
        {
            if (members[i] is object?[] member)
            {
                member[1] = partyId;
                rows[i] = member;
            }
        }

        return rows;
    }

    static DateTime LocalDate(int year, int month, int day, int hour, int minute, int second)
    {
        return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(8)).UtcDateTime;
    }

    static long ReadTripleCastMasterId(object? requestBody)
    {
        if (requestBody is object?[] values)
        {
            return values.ElementAtOrDefault(1) switch
            {
                long id => id,
                int id => id,
                _ => 3033
            };
        }

        return 3033;
    }

    static long ReadTripleCastLiveMasterId(object? requestBody)
    {
        if (requestBody is object?[] values)
        {
            return ConvertToLong(values.ElementAtOrDefault(0))
                ?? throw new BadHttpRequestException(LiveProgressionErrors.InvalidStart);
        }

        throw new BadHttpRequestException(LiveProgressionErrors.InvalidStart);
    }
}
