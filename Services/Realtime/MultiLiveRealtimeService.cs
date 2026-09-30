using System.Collections.Concurrent;
using SiriusLocalServer.Realtime;

sealed class MultiLiveRealtimeService
{
    readonly ConcurrentDictionary<string, MultiLiveRoomState> roomsByHallId = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<long, string> hallIdByMultiLiveId = new();
    readonly ConcurrentDictionary<string, string> hallIdByHashUserId = new(StringComparer.Ordinal);
    readonly LocalRequestLogger logger;

    public MultiLiveRealtimeService(LocalRequestLogger logger)
    {
        this.logger = logger;
    }

    public Task<MultiLiveCreatePrivateHallResult> CreatePrivateHallAsync(
        string hashUserId,
        MultiLiveHallType hallType,
        long liveSettingMasterId,
        string? circleId,
        string userName,
        long userNamePlateColorId,
        MultiLiveCharacter? leaderCharacter)
    {
        var room = CreateRoom(liveSettingMasterId, hallType, isPrivate: true);
        var user = CreateUser(1, hashUserId, userName, userNamePlateColorId, leaderCharacter);
        room.Users[user.MemberId] = user;
        AddNpcUsers(room);
        RegisterRoom(room, hashUserId);

        return Task.FromResult(new MultiLiveCreatePrivateHallResult
        {
            IsSucceeded = true,
            HallId = room.HallId,
            MemberId = user.MemberId,
            PrivateHallKeyCode = room.KeyCode
        });
    }

    public Task<MultiLiveJoinResult> JoinPublicHallAsync(
        string hashUserId,
        MultiLiveHallType hallType,
        long liveSettingMasterId,
        string? circleId,
        string userName,
        long userNamePlateColorId,
        MultiLiveCharacter? leaderCharacter)
    {
        var room = roomsByHallId.Values.FirstOrDefault(x =>
            !x.IsPrivate &&
            x.HallType == hallType &&
            x.LiveSettingMasterId == liveSettingMasterId &&
            x.Status is MultiLiveHallStatus.Recruiting or MultiLiveHallStatus.StandbyGame);

        if (room is null)
        {
            room = CreateRoom(liveSettingMasterId, hallType, isPrivate: false);
            AddNpcUsers(room);
            RegisterRoom(room, hashUserId);
        }

        var existing = room.Users.Values.FirstOrDefault(x => x.HashUserId == hashUserId);
        if (existing is not null)
        {
            hallIdByHashUserId[hashUserId] = room.HallId;
            return Task.FromResult(MultiLiveJoinResult.Success(room.HallId, existing.MemberId, room.KeyCode, room.LiveSettingMasterId));
        }

        var memberId = NextMemberId(room);
        if (memberId > 5)
        {
            return Task.FromResult(MultiLiveJoinResult.Error(MultiLiveJoinErrorCodes.ReachedMaxMember));
        }

        var user = CreateUser(memberId, hashUserId, userName, userNamePlateColorId, leaderCharacter);
        room.Users[user.MemberId] = user;
        hallIdByHashUserId[hashUserId] = room.HallId;

        return Task.FromResult(MultiLiveJoinResult.Success(room.HallId, user.MemberId, room.KeyCode, room.LiveSettingMasterId));
    }

    public Task<MultiLiveJoinResult> JoinPrivateHallWithKeyCodeAsync(
        string hashUserId,
        string? circleId,
        int keyCode,
        string userName,
        long userNamePlateColorId,
        MultiLiveCharacter? leaderCharacter)
    {
        var room = roomsByHallId.Values.FirstOrDefault(x => x.IsPrivate && x.KeyCode == keyCode);
        return room is null
            ? Task.FromResult(MultiLiveJoinResult.Error(MultiLiveJoinErrorCodes.NotFoundHall))
            : JoinRoomAsync(room, hashUserId, userName, userNamePlateColorId, leaderCharacter);
    }

    public Task<MultiLiveJoinResult> JoinPrivateHallFromInviteAsync(
        string hashUserId,
        string? circleId,
        string hallId,
        string userName,
        long userNamePlateColorId,
        MultiLiveCharacter? leaderCharacter)
    {
        return roomsByHallId.TryGetValue(hallId, out var room)
            ? JoinRoomAsync(room, hashUserId, userName, userNamePlateColorId, leaderCharacter)
            : Task.FromResult(MultiLiveJoinResult.Error(MultiLiveJoinErrorCodes.NotFoundHall));
    }

    public Task<MultiLiveFetchUsersResult> FetchUsersAsync(string hashUserId)
    {
        return Task.FromResult(GetRoomForUser(hashUserId)?.ToFetchUsersResult() ?? EmptyFetchUsers());
    }

    public Task SelectMusicAsync(string hashUserId, long musicId, bool isRandom, bool isAFK)
    {
        if (TryGetUser(hashUserId, out _, out var user))
        {
            user.SelectedMusicId = musicId;
            user.IsRandomMusic = isRandom;
            user.IsAFK = isAFK;
            user.IsSelectedMusic = true;
            user.Status = MultiLiveUserStatus.SelectedMusic;
        }

        return Task.CompletedTask;
    }

    public Task SelectDifficultyAsync(string hashUserId, MusicDifficulties difficulty, bool isAFK)
    {
        if (TryGetUser(hashUserId, out _, out var user))
        {
            user.Difficulty = difficulty;
            user.IsAFK = isAFK;
            user.Status = MultiLiveUserStatus.SelectedDifficulty;
        }

        return Task.CompletedTask;
    }

    public Task SelectStampAsync(string hashUserId, long stampId)
    {
        return logger.LogAsync($"realtime-multi-live-stamp hashUserId={hashUserId} stampId={stampId}");
    }

    public Task ReadyGameAsync(string hashUserId)
    {
        if (TryGetUser(hashUserId, out var room, out var user))
        {
            user.Status = MultiLiveUserStatus.ReadyGame;
            foreach (var npc in room.Users.Values.Where(x => x.HashUserId.StartsWith("npc-", StringComparison.Ordinal)))
            {
                npc.Status = MultiLiveUserStatus.ReadyGame;
            }

            room.Status = MultiLiveHallStatus.ReadyGame;
        }

        return Task.CompletedTask;
    }

    public Task BeforeGameCalculateAsync(string hashUserId)
    {
        if (TryGetUser(hashUserId, out var room, out var user))
        {
            user.Status = MultiLiveUserStatus.BeforeGameCalculate;
            foreach (var npc in room.Users.Values.Where(x => x.HashUserId.StartsWith("npc-", StringComparison.Ordinal)))
            {
                npc.Status = MultiLiveUserStatus.BeforeGameCalculate;
            }

            room.Status = MultiLiveHallStatus.BeforeGameCalculate;
        }

        return Task.CompletedTask;
    }

    public Task StartGameAsync(string hashUserId)
    {
        if (TryGetUser(hashUserId, out var room, out var user))
        {
            user.Status = MultiLiveUserStatus.PlayingGame;
            foreach (var npc in room.Users.Values.Where(x => x.HashUserId.StartsWith("npc-", StringComparison.Ordinal)))
            {
                npc.Status = MultiLiveUserStatus.PlayingGame;
            }

            room.Status = MultiLiveHallStatus.PlayingGame;
        }

        return Task.CompletedTask;
    }

    public Task SyncInGameStatusAsync(string hashUserId, int comboCount, ComboTypes comboType, int life)
    {
        if (TryGetUser(hashUserId, out _, out var user))
        {
            user.ComboCount = comboCount;
            user.ComboType = comboType;
            user.Life = life;
        }

        return Task.CompletedTask;
    }

    public Task ExitGameAsync(
        string hashUserId,
        long score,
        ClearLamps clearLamp,
        IReadOnlyDictionary<int, int>? timingCounts,
        int maxCombo)
    {
        if (TryGetUser(hashUserId, out var room, out var user))
        {
            user.Score = score;
            user.ClearLamp = clearLamp;
            user.MaxCombo = maxCombo;
            user.IsExitGame = true;
            user.Status = MultiLiveUserStatus.ExitGame;
            user.TimingCounts.Clear();

            if (timingCounts is not null)
            {
                foreach (var pair in timingCounts)
                {
                    user.TimingCounts[pair.Key] = pair.Value;
                }
            }

            foreach (var npc in room.Users.Values.Where(x => x.HashUserId.StartsWith("npc-", StringComparison.Ordinal)))
            {
                npc.Score = Math.Max(1, score - Random.Shared.Next(10000, 80000));
                npc.ClearLamp = ClearLamps.Clear;
                npc.MaxCombo = Math.Max(1, maxCombo - Random.Shared.Next(20, 180));
                npc.IsExitGame = true;
                npc.Status = MultiLiveUserStatus.ExitGame;
            }
        }

        return Task.CompletedTask;
    }

    public Task EntryFinalResultAsync(string hashUserId)
    {
        if (TryGetUser(hashUserId, out var room, out _))
        {
            room.Status = MultiLiveHallStatus.None;
        }

        return Task.CompletedTask;
    }

    public Task ContinuePlayAsync(string hashUserId)
    {
        if (TryGetUser(hashUserId, out var room, out var user))
        {
            user.IsExitGame = false;
            user.Status = MultiLiveUserStatus.Joined;
            room.Status = MultiLiveHallStatus.StandbyGame;
        }

        return Task.CompletedTask;
    }

    public object?[] GetMultiLiveInformation(long multiLiveId)
    {
        if (hallIdByMultiLiveId.TryGetValue(multiLiveId, out var hallId) &&
            roomsByHallId.TryGetValue(hallId, out var room))
        {
            var map = new Dictionary<string, object?>();
            foreach (var user in room.Users.Values.Where(x => x.Status >= MultiLiveUserStatus.PlayingGame))
            {
                var key = Math.Max(user.MaxCombo, user.MemberId * 13).ToString();
                if (!map.TryGetValue(key, out var bucket) || bucket is not List<object?> list)
                {
                    list = new List<object?>();
                    map[key] = list;
                }

                list.Add(new object?[] { StableIntId(user.HashUserId, 10000000, 99999999), user.HashUserId, int.Parse(key) });
            }

            return new object?[] { map.ToDictionary(x => x.Key, x => (object?)((List<object?>)x.Value!).ToArray()) };
        }

        return Array.Empty<object?>();
    }

    public MultiLiveRoomState? GetRoomByMultiLiveId(long multiLiveId)
    {
        return hallIdByMultiLiveId.TryGetValue(multiLiveId, out var hallId) &&
               roomsByHallId.TryGetValue(hallId, out var room)
            ? room
            : null;
    }

    public MultiLiveRoomState? GetRoomForUser(string hashUserId)
    {
        return hallIdByHashUserId.TryGetValue(hashUserId, out var hallId) &&
               roomsByHallId.TryGetValue(hallId, out var room)
            ? room
            : null;
    }

    public IReadOnlyCollection<MultiLiveRoomState> SnapshotRooms()
    {
        return roomsByHallId.Values.OrderByDescending(x => x.CreatedAt).ToArray();
    }

    async Task<MultiLiveJoinResult> JoinRoomAsync(
        MultiLiveRoomState room,
        string hashUserId,
        string userName,
        long userNamePlateColorId,
        MultiLiveCharacter? leaderCharacter)
    {
        if (room.Users.Values.Any(x => x.HashUserId == hashUserId))
        {
            var existing = room.Users.Values.First(x => x.HashUserId == hashUserId);
            return MultiLiveJoinResult.Success(room.HallId, existing.MemberId, room.KeyCode, room.LiveSettingMasterId);
        }

        var memberId = NextMemberId(room);
        if (memberId > 5)
        {
            return MultiLiveJoinResult.Error(MultiLiveJoinErrorCodes.ReachedMaxMember);
        }

        var user = CreateUser(memberId, hashUserId, userName, userNamePlateColorId, leaderCharacter);
        room.Users[user.MemberId] = user;
        hallIdByHashUserId[hashUserId] = room.HallId;
        await logger.LogAsync($"realtime-multi-live-join hallId={room.HallId} memberId={memberId} hashUserId={hashUserId}");
        return MultiLiveJoinResult.Success(room.HallId, memberId, room.KeyCode, room.LiveSettingMasterId);
    }

    MultiLiveRoomState CreateRoom(long liveSettingMasterId, MultiLiveHallType hallType, bool isPrivate)
    {
        var multiLiveId = liveSettingMasterId switch
        {
            17401 => 11868654,
            7501 => 11868622,
            _ => StableIntId($"{liveSettingMasterId}:{DateTime.UtcNow.Ticks}", 11000000, 11999999)
        };

        return new MultiLiveRoomState
        {
            HallId = $"local-{multiLiveId}",
            KeyCode = Random.Shared.Next(100000, 999999),
            MultiLiveId = multiLiveId,
            LiveSettingMasterId = liveSettingMasterId,
            HallType = hallType,
            IsPrivate = isPrivate,
            Status = MultiLiveHallStatus.Recruiting
        };
    }

    void RegisterRoom(MultiLiveRoomState room, string hashUserId)
    {
        roomsByHallId[room.HallId] = room;
        hallIdByMultiLiveId[room.MultiLiveId] = room.HallId;
        hallIdByHashUserId[hashUserId] = room.HallId;
    }

    static MultiLiveUser CreateUser(
        int memberId,
        string hashUserId,
        string userName,
        long userNamePlateColorId,
        MultiLiveCharacter? leaderCharacter)
    {
        return new MultiLiveUser
        {
            MemberId = memberId,
            HashUserId = string.IsNullOrWhiteSpace(hashUserId) ? $"local-user-{memberId}" : hashUserId,
            UserName = string.IsNullOrWhiteSpace(userName) ? "LocalPlayer" : userName,
            UserNamePlateColorId = userNamePlateColorId,
            LeaderCharacter = leaderCharacter ?? MultiLiveCharacter.Default(110040),
            Difficulty = MusicDifficulties.Extra,
            Status = MultiLiveUserStatus.Joined
        };
    }

    static void AddNpcUsers(MultiLiveRoomState room)
    {
        var names = new[] { "AutoGuestA", "AutoGuestB", "AutoGuestC", "AutoGuestD" };
        var characters = new long[] { 110010, 110030, 110050, 110060 };
        for (var memberId = 2; memberId <= 4; memberId++)
        {
            if (room.Users.ContainsKey(memberId)) continue;
            room.Users[memberId] = CreateUser(
                memberId,
                $"npc-{room.HallId}-{memberId}",
                names[memberId - 2],
                180001,
                MultiLiveCharacter.Default(characters[memberId - 2]));
        }
    }

    static int NextMemberId(MultiLiveRoomState room)
    {
        for (var i = 1; i <= 5; i++)
        {
            if (!room.Users.ContainsKey(i)) return i;
        }

        return 6;
    }

    bool TryGetUser(string hashUserId, out MultiLiveRoomState room, out MultiLiveUser user)
    {
        room = null!;
        user = null!;

        var foundRoom = GetRoomForUser(hashUserId);
        if (foundRoom is null) return false;

        var foundUser = foundRoom.Users.Values.FirstOrDefault(x => x.HashUserId == hashUserId);
        if (foundUser is null) return false;

        room = foundRoom;
        user = foundUser;
        return true;
    }

    static MultiLiveFetchUsersResult EmptyFetchUsers()
    {
        return new MultiLiveFetchUsersResult
        {
            HostMemberId = 0,
            Users = Array.Empty<MultiLiveUser>(),
            CanOpenHall = false
        };
    }

    static int StableIntId(string value, int min, int max)
    {
        var hash = StringComparer.Ordinal.GetHashCode(value);
        var positive = hash == int.MinValue ? 0 : Math.Abs(hash);
        return min + positive % (max - min + 1);
    }
}
