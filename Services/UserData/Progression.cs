using System.Security.Cryptography;
using Npgsql;

sealed partial class UserDataService
{
    public sealed record LiveSettlement(object?[] Result, object?[] Present);

    public async Task RegisterLiveStartAsync(HttpContext context, object? payload, bool validateMusicUnlock = true)
    {
        if (payload is not object?[] start || start.Length < 8 ||
            !long.TryParse(start[1]?.ToString(), out var chartId) || !PlayerProgressionRules.Charts.ContainsKey(chartId) ||
            start[7] is not bool auto || start[4] is not bool useStamina)
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidStart);
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException("ACCOUNT_UNAUTHORIZED", 401);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        var missionParty = await ReadMissionPartyAsync(connection,userId,GetLong(start,0) ?? 0);
        if (missionParty.Length == 0 && GetLong(start,0) is > 0)
            context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogWarning(
                "演出缺少可持久化角色编队 userId={UserId} partyId={PartyId} operation={Operation}",userId,GetLong(start,0),"character-mission-snapshot");
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        if (validateMusicUnlock) await ValidateMusicDifficultyAsync(connection, userId, PlayerProgressionRules.Charts[chartId], auto);
        await using var command = new NpgsqlCommand(
            """
            insert into user_live_sessions ("userId", live_master_id, is_auto, use_stamina, character_mission_party)
            values ($1, $2, $3, $4, $5)
            on conflict ("userId") do update set live_master_id = excluded.live_master_id,
              is_auto = excluded.is_auto, use_stamina = excluded.use_stamina,
              completion = null, finish_hash = null, started_at = now(),character_mission_party=excluded.character_mission_party
            """, connection, transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(chartId);
        command.Parameters.AddWithValue(auto);
        command.Parameters.AddWithValue(useStamina);
        command.Parameters.AddWithValue(MsgPack.Encode(missionParty));
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    static async Task NormalizeRankAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        var state = await ReadRankStateAsync(connection, transaction, userId);
        var next = PlayerProgressionRules.Advance(state.Rank, state.Exp, 0, state.Limit);
        if (next.Rank == state.Rank && next.Exp == state.Exp && next.MaxStamina == state.MaxStamina) return;
        await ExecuteAsync(connection, transaction,
            """
            update user_game_states set rank = $2, exp = $3, max_stamina = $4,
              stamina = stamina + $5, updated_at = now() where "userId" = $1
            """, userId, next.Rank, next.Exp, next.MaxStamina, next.StaminaRecovery);
    }

    static async Task<(int Rank, int Exp, int Stamina, int Limit, int MaxStamina)> ReadRankStateAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        await using var command = new NpgsqlCommand(
            "select rank, exp, stamina, rank_limit, max_stamina from user_game_states where \"userId\" = $1 for update", connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("缺少玩家等级状态");
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
    }

    static async Task<double> ReadPlayerRatingAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select coalesce(round(sum(notation_rate)::numeric, 2), 0)::double precision from (
              select notation_rate from user_live_play_results
              where "userId" = $1 and difficulty between 1 and 4 and notation_rate > 0
              order by notation_rate desc, live_master_id limit 30
            ) best
            """, connection);
        command.Parameters.AddWithValue(userId);
        return Convert.ToDouble(await command.ExecuteScalarAsync());
    }

    public async Task<LiveSettlement> BuildLiveSettlementAsync(HttpContext context, object? payload)
    {
        if (payload is object?[] rawFinish) payload = LiveFinishValidation.Normalize(rawFinish);
        if (payload is not object?[] finish || finish.Length < 4 ||
            !long.TryParse(finish[0]?.ToString(), out var score) || score < 0 || finish[3] is not bool cleared)
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidFinish);
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException("ACCOUNT_UNAUTHORIZED", 401);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        // 两种演出共用结束接口，按最近一次开始的会话分流，不能命中历史普通演出。
        await using (var mode = new NpgsqlCommand(
            """
            select exists(select 1 from user_lesson_sessions l where l."userId"=$1
              and not exists(select 1 from user_live_sessions v where v."userId"=l."userId" and v.started_at>=l.started_at))
            """, connection, transaction))
        {
            mode.Parameters.AddWithValue(userId);
            if ((bool)(await mode.ExecuteScalarAsync())!)
            {
                var lesson = await FinishLessonCoreAsync(connection, transaction, userId, new object?[] { finish[0], finish[1], finish[2] });
                await transaction.CommitAsync();
                context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
                    "稽古结算成功 userId={UserId} operation={Operation} score={Score}", userId, "lesson-finish-live", score);
                return lesson;
            }
        }
        long chartId;
        bool auto, useStamina;
        object?[] missionParty;
        var hash = SHA256.HashData(MsgPack.Encode(payload));
        await using (var command = new NpgsqlCommand(
            "select live_master_id, is_auto, use_stamina, completion, finish_hash,character_mission_party from user_live_sessions where \"userId\" = $1 for update", connection, transaction))
        {
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new BadHttpRequestException(LiveProgressionErrors.SessionMissing, 409);
            if (!reader.IsDBNull(3))
            {
                if (!hash.AsSpan().SequenceEqual(reader.GetFieldValue<byte[]>(4)))
                    throw new BadHttpRequestException(LiveProgressionErrors.SessionConflict, 409);
                var cached = (object?[])MsgPack.Decode(reader.GetFieldValue<byte[]>(3))!;
                // 结算奖励沿用幂等缓存，谱面状态取当前存档，避免迟到重试覆盖后来购买的解锁。
                await reader.DisposeAsync();
                var replay = ((object?[])cached[1]!).Cast<object?[]>()
                    .Where(row=>Convert.ToInt32(row[0]) is not (25 or 130)).Cast<object?>().ToList();
                replay.AddRange((await ReadLiveMusicStatesAsync(connection,userId)).Select(row=>DataObject(25,row)));
                replay.Add(DataObject(130,await ReadLiveAchievementAsync(connection,userId)));
                return new((object?[])cached[0]!, replay.ToArray());
            }
            chartId = reader.GetInt64(0); auto = reader.GetBoolean(1); useStamina = reader.GetBoolean(2);
            // 旧会话没有队伍快照，不能凭当前编队补写无法核实的历史角色进度。
            missionParty = reader.IsDBNull(5) ? Array.Empty<object?>() : (object?[])MsgPack.Decode(reader.GetFieldValue<byte[]>(5))!;
        }
        var chart = PlayerProgressionRules.Charts[chartId];
        if (!auto) await RecordCharacterMissionLiveAsync(connection,transaction,userId,missionParty,finish,cleared);
        var state = await ReadRankStateAsync(connection, transaction, userId);
        var gain = cleared && useStamina ? CapturedLiveExpGain : 0;
        var coins = cleared ? CapturedLiveCoinGain : 0;
        var next = PlayerProgressionRules.Advance(state.Rank, state.Exp, gain, state.Limit);
        var beforeRating = await ReadPlayerRatingAsync(connection, userId);
        decimal achievement = 0, notation = 0;
        var lamp = 0;
        double bestAchievement = 0, bestNotation = 0;
        var beforeLamp = 0;
        if (!auto)
        {
            var judges = ReadFinishJudges(finish);
            achievement = PlayerProgressionRules.Achievement(judges);
            lamp = cleared ? judges.Where(pair => pair.Key < 5).Sum(pair => pair.Value) == 0 ? 6 :
                judges.Where(pair => pair.Key <= 2).Sum(pair => pair.Value) == 0 ? 2 : 1 : 0;
            var alive = FinishLifeIsPositive(finish);
            notation = cleared && alive && chart.Difficulty <= 4 ? PlayerProgressionRules.NotationRate(chart.Level, achievement) : 0;
            await using (var previous = new NpgsqlCommand(
                "select achievement_rate, notation_rate, clear_lamp from user_live_play_results where \"userId\" = $1 and live_master_id = $2 and difficulty = $3", connection, transaction))
            {
                previous.Parameters.AddWithValue(userId); previous.Parameters.AddWithValue(chartId); previous.Parameters.AddWithValue(chart.Difficulty);
                await using var reader = await previous.ExecuteReaderAsync();
                if (await reader.ReadAsync()) { bestAchievement = reader.GetDouble(0); bestNotation = reader.GetDouble(1); beforeLamp = reader.GetInt32(2); }
            }
            await SaveChartResultAsync(connection, transaction, userId, chart, score, achievement, notation, lamp, chart.Difficulty==5 ? cleared && alive : cleared);
        }
        await ExecuteAsync(connection, transaction,
            """
            update user_game_states set rank = $2, exp = $3, max_stamina = $4,
              stamina = greatest(stamina - $5, 0) + $6, coin = coin + $7, updated_at = now()
            where "userId" = $1
            """, userId, next.Rank, next.Exp, next.MaxStamina, cleared && useStamina ? 1 : 0, next.StaminaRecovery, coins);
        await ExecuteAsync(connection, transaction, "update user_item_currencies set coin = coin + $2 where \"userId\" = $1", userId, coins);
        var rewards = cleared ? CapturedLiveItemRewards : Array.Empty<(long ItemMasterId, int Quantity)>();
        foreach (var reward in rewards) await UpsertItemQuantityAsync(connection, transaction, userId, reward.ItemMasterId, reward.Quantity);
        await RecordDifficultyUnlockAsync(connection,transaction,userId,chart,finish,auto,cleared,achievement);
        await SynchronizeMusicUnlocksAsync(connection,transaction,userId);
        var rating = await ReadPlayerRatingAsync(connection, userId);
        await ExecuteAsync(connection, transaction, "update user_live_user_stats set score_rating = $2 where \"userId\" = $1", userId, rating);
        // 不再向普通演出注入抓包里的活动、试镜和任务进度。
        var result = new object?[31];
        result[1] = Array.Empty<object?>(); result[9] = 0; result[10] = 0;
        result[11] = Array.Empty<object?>(); result[12] = Array.Empty<object?>();
        result[21] = 0; result[30] = Array.Empty<object?>();
        var drops = rewards.Select((reward, index) => (object?)new object?[]
        {
            new object?[] { 1, reward.ItemMasterId, reward.Quantity, null, null, null, false }, index + 101, 0, false
        }).ToList();
        if (coins > 0) drops.Add(new object?[] { new object?[] { 12, 0, coins, null, null, null, false }, 0, 0, false });
        result[23] = drops.ToArray();
        result[2] = new object?[] { new object?[] { bestAchievement, (double)achievement }, new object?[] { bestNotation, (double)notation }, beforeRating, rating };
        result[3] = new object?[] { state.Rank, next.Rank, state.Exp, next.Exp, gain, state.Stamina };
        result[5] = lamp; result[6] = auto ? 0 : PlayerProgressionRules.Grade(achievement);
        result[8] = false; result[13] = (double)achievement; result[20] = beforeLamp;
        if (!cleared) result[23] = Array.Empty<object?>();
        var present = new List<object?> { DataObject(0, await ReadUserAsync(connection, userId)), DataObject(1, await ReadProfileAsync(connection, userId)), DataObject(128, await ReadCurrencyAsync(connection, userId)) };
        if (!auto)
        {
            foreach (var row in await ReadLivePlayResultsAsync(connection, userId))
                if (Convert.ToInt64(row[1]) == chartId) present.Add(DataObject(24, row));
        }
        foreach (var row in await ReadItemPossessionsAsync(connection, userId))
            if (rewards.Any(reward => reward.ItemMasterId == Convert.ToInt64(row[1]))) present.Add(DataObject(27, row));
        present.AddRange((await ReadCharacterMissionsAsync(connection,userId)).Select(row => DataObject(96,row)));
        var musicStates = await ReadLiveMusicStatesAsync(connection,userId);
        present.AddRange(musicStates.Select(row=>DataObject(25,row)));
        present.Add(DataObject(130,await ReadLiveAchievementAsync(connection,userId)));
        var settlement = new LiveSettlement(result,present.ToArray());
        await ExecuteAsync(connection, transaction,
            "update user_live_sessions set completion = $2, finish_hash = $3 where \"userId\" = $1",
            userId, MsgPack.Encode(new object?[] { settlement.Result, settlement.Present }), hash);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "Live settlement userId={UserId} chartId={ChartId} auto={Auto} cleared={Cleared} score={Score} achievement={Achievement} rank={Rank} exp={Exp} rating={Rating} stellaReleased={StellaReleased} olivierStatus={OlivierStatus}",
            userId, chartId, auto, cleared, score, achievement, next.Rank, next.Exp, rating,
            musicStates.Single(row=>Convert.ToInt64(row[2])==chart.MusicMasterId)[5],
            musicStates.Single(row=>Convert.ToInt64(row[2])==chart.MusicMasterId)[8]);
        return settlement;
    }

    static Dictionary<int, int> ReadFinishJudges(object?[] finish)
    {
        if (finish[2] is not object?[] rows) throw new BadHttpRequestException(LiveProgressionErrors.InvalidJudges);
        var judges = new Dictionary<int, int>();
        foreach (var value in rows)
        {
            if (value is not object?[] row || row.Length != 2 || !int.TryParse(row[0]?.ToString(), out var timing) ||
                !int.TryParse(row[1]?.ToString(), out var count) || !judges.TryAdd(timing, count))
                throw new BadHttpRequestException(LiveProgressionErrors.InvalidJudges);
        }
        return judges;
    }

    static bool FinishLifeIsPositive(object?[] finish)
    {
        if (finish.Length <= 4 || finish[4] is not object?[] blocks || blocks.Length == 0 ||
            blocks[^1] is not object?[] last || last.Length < 3 || !int.TryParse(last[2]?.ToString(), out var life))
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidFinish);
        return life > 0;
    }

    static Task SaveChartResultAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId,
        PlayerProgressionRules.Chart chart, long score, decimal achievement, decimal notation, int lamp, bool cleared) =>
        ExecuteAsync(connection, transaction,
            """
            insert into user_live_play_results (id, "userId", live_master_id, difficulty, best_score,
              achievement_rate, notation_rate, clear_lamp, rate_grade, clear_count, full_combo_count, all_perfect_count)
            values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)
            on conflict ("userId", live_master_id, difficulty) do update set
              best_score = greatest(user_live_play_results.best_score, excluded.best_score),
              achievement_rate = greatest(user_live_play_results.achievement_rate, excluded.achievement_rate),
              notation_rate = greatest(user_live_play_results.notation_rate, excluded.notation_rate),
              clear_lamp = greatest(user_live_play_results.clear_lamp, excluded.clear_lamp),
              rate_grade = greatest(user_live_play_results.rate_grade, excluded.rate_grade),
              clear_count = user_live_play_results.clear_count + excluded.clear_count,
              full_combo_count = user_live_play_results.full_combo_count + excluded.full_combo_count,
              all_perfect_count = user_live_play_results.all_perfect_count + excluded.all_perfect_count,
              played_at = now(), updated_at = now()
            """, UserScopedId(userId, chart.Id), userId, chart.Id, chart.Difficulty, (double)score,
            (double)achievement, (double)notation, lamp, PlayerProgressionRules.Grade(achievement), cleared ? 1 : 0, lamp >= 2 ? 1 : 0, lamp == 6 ? 1 : 0);
}
