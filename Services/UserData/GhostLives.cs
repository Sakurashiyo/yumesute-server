using Npgsql;

sealed partial class UserDataService
{
    static readonly Dictionary<long, object?[]> GhostMasters = GachaRules.ReadRows("Live.GhostLiveMaster.json")
        .Cast<object?[]>().ToDictionary(row => Convert.ToInt64(row[0]));

    public async Task<object?[]> MatchGhostLiveAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException(AccountErrors.Unauthorized, 401);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        // 开演后的匹配重试沿用本场冻结对手，不能改变结算目标。
        await using (var pending = new NpgsqlCommand(
            "select mode_context from user_live_sessions where \"userId\"=$1 and live_mode=$2 and completion is null", connection, transaction))
        {
            pending.Parameters.AddWithValue(userId);
            pending.Parameters.AddWithValue((int)LiveRewardRules.Mode.Ghost);
            if (await pending.ExecuteScalarAsync() is byte[] bytes)
                return (object?[])((object?[])MsgPack.Decode(bytes)!)[0]!;
        }
        var now = DateTime.UtcNow;
        var available = GhostMasters.Values.Where(row => row[4] is DateTime begin && row[5] is DateTime end
                && begin <= now && now < end
                && PlayerProgressionRules.Charts.Values.Any(c => c.MusicMasterId == Convert.ToInt64(row[1]) && c.Difficulty <= 4))
            .OrderBy(row => Convert.ToInt64(row[0])).ToArray();
        if (available.Length == 0) throw new BadHttpRequestException(LiveProgressionErrors.GhostUnavailable, 404);
        var master = available[(int)((userId + DateTime.UtcNow.Date.Ticks / TimeSpan.TicksPerDay) % available.Length)];
        var members = new long[] { 120010, 120040, 120050, 120060, 120030 }.Select((id, index) =>
        {
            var card = new object?[] { index + 1, id, 50, 0, 0, 0, 0, 1, 0, 0, false, null, 0, 1, false };
            return (Card: card, Status: CharacterStatusRules.Calculate(card, 1, 0));
        }).ToArray();
        var profile = BuildUserProfileDetail("0000000000", "Ghost", Convert.ToInt64(members[0].Card[1]), 50,
            false, DefaultIconFrameMasterId, DefaultNameColorMasterId, DefaultNameBaseColorMasterId, null);
        var slots = members.Select((member, index) => (object?)new object?[] { index + 1, member.Card[1], 50, 0, 0, false,
            null, null, null, null, null, member.Status[0], member.Status[1], member.Status[2], null }).ToArray();
        object?[] matching = [master[0], profile, new object?[] { 1, slots }];
        // 本服 NPC 采用队伍属性十倍作为目标基础分；该策略不是官方 Ghost 成绩模拟。
        var targetScore = checked(members.Sum(m => Convert.ToInt64(m.Status[3])) * 10);
        await ExecuteAsync(connection, transaction, """
            insert into user_ghost_lives("userId",matching,rival_score) values($1,$2,$3)
            on conflict("userId") do update set matching=excluded.matching,rival_score=excluded.rival_score
            """, userId, MsgPack.Encode(matching), targetScore);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "Ghost 匹配 operation=ghost-match userId={UserId} ghostMasterId={GhostMasterId} rivalScore={RivalScore}",
            userId, master[0], targetScore);
        return matching;
    }

    static async Task<object?[]> ReadGhostStartAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long userId, object?[] payload, long chartId)
    {
        if (GetLong(payload, 13) is not long masterId || !GhostMasters.TryGetValue(masterId, out var master)
            || Convert.ToInt64(master[1]) != PlayerProgressionRules.Charts[chartId].MusicMasterId
            || payload[7] is not false)
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidStart);
        await using var command = new NpgsqlCommand(
            "select matching,rival_score from user_ghost_lives where \"userId\"=$1", connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new BadHttpRequestException(LiveProgressionErrors.SessionMissing, 409);
        var match = (object?[])MsgPack.Decode(reader.GetFieldValue<byte[]>(0))!;
        if (Convert.ToInt64(match[0]) != masterId)
            throw new BadHttpRequestException(LiveProgressionErrors.SessionConflict, 409);
        return [match, reader.GetInt64(1)];
    }

    static async Task<object?[]> FinishGhostAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long userId, object?[] context, long score)
    {
        var match = (object?[])context[0]!;
        var target = Convert.ToInt64(context[1]);
        var outcome = score > target ? 0 : score < target ? 1 : 2;
        await using var command = new NpgsqlCommand("""
            update user_ghost_lives set win_count=win_count+$2 where "userId"=$1 returning win_count
            """, connection, transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(outcome == 0 ? 1 : 0);
        var wins = await command.ExecuteScalarAsync() ?? throw new InvalidDataException("Ghost 结算缺少匹配存档");
        var profile = (object?[])match[1]!;
        return [profile, target, wins, outcome, profile[5], profile[7]];
    }
}
