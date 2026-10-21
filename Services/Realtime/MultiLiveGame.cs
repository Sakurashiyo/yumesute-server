using Grpc.Core;
using SiriusLocalServer.Realtime;

// 房间中的阶段屏障由服务端统一推进，避免任一客户端提前把其他成员带入下一阶段。
sealed partial class MultiLiveRealtimeService
{
    static void RequireState(bool condition)
    {
        if (!condition) throw new RpcException(new Status(StatusCode.FailedPrecondition, RealtimeHubProtocol.RoomStateConflict));
    }

    bool HandleGame(MultiLiveRoomState room, MultiLiveUser user, string method, object? args, out object? result)
    {
        result = null;
        if (method is "OpenPrivateRoomAsync" or "DecideMemberAsync")
        {
            if (args is not null) throw MultiLiveWire.Invalid();
            if (user.MemberId != room.HostMemberId)
                throw new RpcException(new Status(StatusCode.PermissionDenied, RealtimeHubProtocol.HostRequired));
            if (method == "OpenPrivateRoomAsync")
            {
                RequireState(room.Status == MultiLiveHallStatus.Recruiting);
                room.IsPrivate = false;
                return true;
            }
            if (room.Status == MultiLiveHallStatus.StandbyGame) return true;
            RequireState(room.Status == MultiLiveHallStatus.Recruiting);
            room.Status = MultiLiveHallStatus.StandbyGame;
            Broadcast(room, "OnCountDownDecideMember", null);
            Broadcast(room, "OnReadyGroup", room.IsPrivate);
            return true;
        }
        if (method is "ReadyGameAsync" or "BeforeGameCalculateAsync" or "StartGameAsync")
        {
            if (args is not null) throw MultiLiveWire.Invalid();
            var target = method switch
            {
                "ReadyGameAsync" => MultiLiveUserStatus.ReadyGame,
                "BeforeGameCalculateAsync" => MultiLiveUserStatus.BeforeGameCalculate,
                _ => MultiLiveUserStatus.PlayingGame
            };
            if (user.Status >= target) return true;
            RequireState(room.MusicMasterId > 0 && (int)user.Status == (int)target - 1);
            var requiredRoomStatus = target switch
            {
                MultiLiveUserStatus.ReadyGame => MultiLiveHallStatus.StandbyGame,
                MultiLiveUserStatus.BeforeGameCalculate => MultiLiveHallStatus.ReadyGame,
                _ => MultiLiveHallStatus.BeforeGameCalculate
            };
            RequireState(room.Status == requiredRoomStatus);
            user.Status = target;
            AdvanceGame(room);
            return true;
        }
        if (method == "SyncInGameStatusAsync")
        {
            var a = MultiLiveWire.Args(args, 3);
            RequireState(room.Status == MultiLiveHallStatus.PlayingGame && !user.IsExitGame);
            user.ComboCount = (int)MultiLiveWire.Number(a[0], 0, int.MaxValue);
            user.ComboType = (ComboTypes)MultiLiveWire.Number(a[1], 0, 2);
            user.Life = (int)MultiLiveWire.Number(a[2], 0, int.MaxValue);
            Broadcast(room, "OnSyncInGameStatus", new object?[] { user.MemberId, user.ComboCount, (int)user.ComboType, user.Life }, user.HashUserId);
            return true;
        }
        if (method == "ExitGameAsync")
        {
            var a = MultiLiveWire.Args(args, 4);
            var score = MultiLiveWire.Number(a[0]);
            var lamp = (ClearLamps)MultiLiveWire.Number(a[1], 0, 9);
            var combo = (int)MultiLiveWire.Number(a[3], 0, int.MaxValue);
            if (a[2] is not Dictionary<string, object?> counts || counts.Count is < 1 or > 7)
                throw MultiLiveWire.Invalid();
            var timings = new Dictionary<int, int>();
            foreach (var pair in counts)
            {
                if (!int.TryParse(pair.Key, out var key) || key is < 0 or > 6) throw MultiLiveWire.Invalid();
                timings.Add(key, (int)MultiLiveWire.Number(pair.Value, 0, int.MaxValue));
            }
            if (user.IsExitGame)
            {
                RequireState(user.Score == score && user.ClearLamp == lamp && user.MaxCombo == combo
                    && timings.Count == user.TimingCounts.Count && timings.All(p => user.TimingCounts.GetValueOrDefault(p.Key, -1) == p.Value));
                return true;
            }
            RequireState(room.Status == MultiLiveHallStatus.PlayingGame && user.Status == MultiLiveUserStatus.PlayingGame);
            user.Score = score; user.ClearLamp = lamp; user.MaxCombo = combo;
            user.IsExitGame = true; user.Status = MultiLiveUserStatus.ExitGame;
            foreach (var pair in timings) user.TimingCounts.Add(pair.Key, pair.Value);
            Broadcast(room, "OnSyncGameResult", new object?[] { user.MemberId, timings, combo });
            Broadcast(room, "OnExitGameAnyOne", new object?[] { room.Users.Count, room.Users.Values.Count(u => u.IsExitGame) });
            AdvanceGame(room);
            return true;
        }
        if (method == "EntryFinalResultAsync")
        {
            if (args is not null) throw MultiLiveWire.Invalid();
            RequireState(room.ResultsPublished && user.IsExitGame);
            user.EnteredFinalResult = true;
            return true;
        }
        if (method == "ContinuePlayAsync")
        {
            if (args is not null) throw MultiLiveWire.Invalid();
            RequireState(room.ResultsPublished && user.EnteredFinalResult);
            // 每位玩家重新入房，旧局仍保留，尚未读完结算的玩家不会被清空状态。
            var identity = user.HashUserId;
            var name = user.UserName; var color = user.UserNamePlateColorId; var character = user.LeaderCharacter;
            LeaveRoom(identity);
            var next = room.ContinueHallId is { } nextId ? roomsByHallId.GetValueOrDefault(nextId) : null;
            if (next is null)
            {
                next = CreateRoom(room.LiveSettingMasterId, room.HallType, room.IsPrivate);
                RegisterRoom(next, identity);
                room.ContinueHallId = next.HallId;
            }
            var joined = JoinRoomAsync(next, identity, name, color, character).GetAwaiter().GetResult();
            Broadcast(next, "OnJoin", MultiLiveWire.User(next.Users[joined.MemberId]), identity);
            result = MultiLiveWire.Join(joined);
            return true;
        }
        return false;
    }

    void AdvanceGame(MultiLiveRoomState room)
    {
        var users = room.Users.Values.ToArray();
        if (users.Length == 0) return;
        if (room.Status == MultiLiveHallStatus.StandbyGame && room.MusicMasterId == 0 && users.All(u => u.IsSelectedMusic))
        {
            var choices = users.Where(u => !u.IsRandomMusic).Select(u => u.SelectedMusicId).ToArray();
            if (choices.Length == 0) choices = PlayerProgressionRules.Charts.Values.Where(c => c.Difficulty == 1)
                .Select(c => c.MusicMasterId).Distinct().ToArray();
            room.MusicMasterId = choices[Random.Shared.Next(choices.Length)];
            Broadcast(room, "OnLotMusic", new object?[] { room.MusicMasterId, room.MultiLiveId });
        }
        if (room.Status == MultiLiveHallStatus.StandbyGame && users.All(u => u.Status >= MultiLiveUserStatus.ReadyGame))
        {
            room.Status = MultiLiveHallStatus.ReadyGame;
            Broadcast(room, "OnGoGame", null);
        }
        if (room.Status == MultiLiveHallStatus.ReadyGame && users.All(u => u.Status >= MultiLiveUserStatus.BeforeGameCalculate))
        {
            room.Status = MultiLiveHallStatus.BeforeGameCalculate;
            Broadcast(room, "OnBeforeGameCalculate", null);
        }
        if (room.Status == MultiLiveHallStatus.BeforeGameCalculate && users.All(u => u.Status >= MultiLiveUserStatus.PlayingGame))
        {
            room.Status = MultiLiveHallStatus.PlayingGame;
            Broadcast(room, "OnPlayGame", null);
        }
        if (room.Status == MultiLiveHallStatus.PlayingGame && !room.ResultsPublished && users.All(u => u.IsExitGame))
        {
            room.ResultsPublished = true;
            var best = users.Max(u => u.Score);
            var mvp = users.Where(u => u.Score == best).Select(u => (object?)u.MemberId).ToArray();
            Broadcast(room, "OnExitAllGames", new object?[] { mvp, MultiLiveWire.Users(room.ToFetchUsersResult()) });
        }
    }

    public long ValidateLiveStart(string identity, long liveId, long chartId)
    {
        lock (SyncRoot)
        {
            var room = GetRoomForUser(identity);
            if (room is null || room.MultiLiveId != liveId
                || !PlayerProgressionRules.Charts.TryGetValue(chartId, out var chart)
                || room.MusicMasterId != chart.MusicMasterId
                || (int)room.Users.Values.Single(u => u.HashUserId == identity).Difficulty != chart.Difficulty
                || room.Status is < MultiLiveHallStatus.ReadyGame or > MultiLiveHallStatus.PlayingGame
                || room.ResultsPublished)
                throw new BadHttpRequestException(LiveProgressionErrors.SessionConflict, 409);
            return room.LiveSettingMasterId;
        }
    }

    public object?[] ReadLiveInformation(string identity, long liveId)
    {
        lock (SyncRoot)
        {
            var room = GetRoomForUser(identity);
            if (room is null || room.MultiLiveId != liveId)
                throw new BadHttpRequestException(LiveProgressionErrors.SessionMissing, 404);
            // 尚未还原跨玩家 AdditionalScore，空整数键映射不会注入抓包中的陌生玩家和虚假加分。
            return [new Dictionary<int, object?>()];
        }
    }
}
