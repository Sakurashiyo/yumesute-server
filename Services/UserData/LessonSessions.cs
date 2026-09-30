using Npgsql;

sealed partial class UserDataService
{
    sealed record LessonSession(long BaseId, long LiveId, DateTime StartedAt, object?[] Party, byte[]? Completion, byte[]? FinishHash);

    static async Task LockLessonUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        await using var command = new NpgsqlCommand("select \"userId\" from user_game_states where \"userId\" = $1 for update", connection, transaction);
        command.Parameters.AddWithValue(userId);
        if (await command.ExecuteScalarAsync() is not long) throw new InvalidOperationException("缺少玩家状态");
    }

    static async Task<LessonSession?> ReadLessonSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        await using var command = new NpgsqlCommand("select character_base_master_id, live_master_id, started_at, party_snapshot, completion, finish_hash from user_lesson_sessions where \"userId\" = $1 for update", connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetDateTime(2),
            (object?[])MsgPack.Decode(reader.GetFieldValue<byte[]>(3))!,
            reader.IsDBNull(4) ? null : reader.GetFieldValue<byte[]>(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<byte[]>(5));
    }

    public async Task<object?[]> StartLessonAsync(HttpContext context, long characterBaseMasterId, long liveMasterId, object? payload)
    {
        ValidateLessonStart(characterBaseMasterId, liveMasterId, payload);
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException(PartyNotFound, 404);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var now = DateTimeOffset.UtcNow;
        var boundary = CurrentDailyResetBoundaryUtc(now).UtcDateTime;
        var daily = await ReadDailyLimitAsync(connection, userId, transaction, now);
        var session = await ReadLessonSessionAsync(connection, transaction, userId);
        if (session is { Completion: null } && (session.StartedAt >= boundary || (session.BaseId == characterBaseMasterId && session.LiveId == liveMasterId)))
        {
            if (session.BaseId != characterBaseMasterId || session.LiveId != liveMasterId)
                throw new BadHttpRequestException(LessonErrors.SessionConflict, 409);
            await transaction.CommitAsync();
            return new object?[] { DataObject(109, daily) };
        }
        var party = (await ReadLessonPartiesAsync(connection, userId, transaction))
            .SingleOrDefault(row => Convert.ToInt64(row[0]) == characterBaseMasterId)
            ?? throw new BadHttpRequestException(PartyNotFound, 404);
        if (Convert.ToInt32(daily[2]) >= 1) throw new BadHttpRequestException(LessonErrors.DailyLimitReached, 409);
        var ownedCards = (await ReadCharactersAsync(connection, userId)).ToDictionary(row => Convert.ToInt64(row[0]));
        var cards = ((object?[][])party[1]!).Where(slot => slot[1] is not null).Select(slot =>
        {
            var cardId = Convert.ToInt64(slot[1]);
            return ownedCards.TryGetValue(cardId, out var card) ? card : throw new BadHttpRequestException(PartyMemberInvalid, 400);
        }).ToArray();
        if (cards.Length == 0) throw new BadHttpRequestException(PartyMemberInvalid, 400);
        var snapshot = new object?[] { party, cards, await ReadLessonCharacterStatusesAsync(connection, transaction, userId, cards) };
        await ExecuteAsync(connection, transaction,
            "update user_daily_limits set daily_lesson_times = daily_lesson_times + 1, updated_at = now() where \"userId\" = $1", userId);
        await ExecuteAsync(connection, transaction,
            """
            insert into user_lesson_sessions ("userId", character_base_master_id, live_master_id, started_at, party_snapshot)
            values ($1,$2,$3,$4,$5)
            on conflict ("userId") do update set character_base_master_id=excluded.character_base_master_id,
              live_master_id=excluded.live_master_id, started_at=excluded.started_at, party_snapshot=excluded.party_snapshot,
              completion=null, finish_hash=null
            """, userId, characterBaseMasterId, liveMasterId, now.UtcDateTime, MsgPack.Encode(snapshot));
        daily[2] = Convert.ToInt32(daily[2]) + 1;
        await transaction.CommitAsync();
        return new object?[] { DataObject(109, daily) };
    }

    public async Task<LiveSettlement> StartLessonLiveAsync(HttpContext context, object? payload)
    {
        if (payload is not object?[] { Length: 2 } values || GetLong(values, 0) is not long baseId || GetLong(values, 1) is not long liveId)
            throw new BadHttpRequestException(LessonErrors.InvalidStart, 400);
        var present = await StartLessonAsync(context, baseId, liveId, payload);
        // 新旧开始入口共用场次与次数扣除；演出结果沿用现有客户端兼容结构。
        return new LiveSettlement(await ReadLessonLiveUnitAsync(context), present);
    }

    static void ValidateLessonStart(long characterBaseMasterId, long liveMasterId, object? payload)
    {
        if (payload is not object?[] { Length: 2 } values || GetLong(values, 0) != characterBaseMasterId
            || GetLong(values, 1) != liveMasterId || !CharacterCatalog.Value.Bases.ContainsKey(characterBaseMasterId)
            || !PlayerProgressionRules.Charts.ContainsKey(liveMasterId))
            throw new BadHttpRequestException(LessonErrors.InvalidStart, 400);
    }
}
