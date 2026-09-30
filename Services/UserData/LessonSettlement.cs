using System.Security.Cryptography;
using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> FinishLessonAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException(PartyNotFound, 404);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var settlement = await FinishLessonCoreAsync(connection, transaction, userId, payload);
        await transaction.CommitAsync();
        return settlement.Present;
    }

    async Task<LiveSettlement> FinishLessonCoreAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, object? payload)
    {
        if (payload is not object?[] { Length: 3 } finish || GetLong(finish, 0) is not long score || score < 0
            || GetLong(finish, 1) is not long combo || combo is < 0 or > int.MaxValue)
            throw new BadHttpRequestException(LessonErrors.InvalidFinish, 400);
        var judges = ReadFinishJudges(finish);
        PlayerProgressionRules.Achievement(judges);
        if (combo > judges.Values.Sum(count => (long)count)) throw new BadHttpRequestException(LessonErrors.InvalidFinish, 400);
        var hash = SHA256.HashData(MsgPack.Encode(payload));
        var session = await ReadLessonSessionAsync(connection, transaction, userId)
            ?? throw new BadHttpRequestException(LessonErrors.SessionMissing, 409);
        if (session.Completion is not null)
        {
            if (!hash.SequenceEqual(session.FinishHash!)) throw new BadHttpRequestException(LessonErrors.SessionConflict, 409);
            var cached = (object?[])MsgPack.Decode(session.Completion)!;
            if (cached.Length == 2)
            {
                var cachedResult = (object?[])cached[0]!;
                if (cachedResult[2] is null)
                {
                    // 补齐旧结算缓存缺失的显示字段，保留奖励和高分结果，不重新结算。
                    cachedResult[2] = await BuildLessonRateResultAsync(connection, userId, PlayerProgressionRules.Achievement(judges));
                    await ExecuteAsync(connection, transaction, "update user_lesson_sessions set completion=$2 where \"userId\"=$1",
                        userId, MsgPack.Encode(cached));
                }
                return new(cachedResult, (object?[])cached[1]!);
            }
            // 兼容修复前仅保存 Present 的已完成稽古，不重新发放奖励。
            var cachedParty = (object?[])cached.Cast<object?[]>().Single(row => Convert.ToInt32(row[0]) == 39)[1]!;
            return new(await BuildLessonFinishResultAsync(connection, userId, session.BaseId, score, judges,
                Convert.ToInt64(cachedParty[2]), Convert.ToInt64(cachedParty[2]), Array.Empty<LessonRules.Reward>()), cached);
        }
        var party = (await ReadLessonPartiesAsync(connection, userId, transaction)).Single(row => Convert.ToInt64(row[0]) == session.BaseId);
        var previousScore = Convert.ToInt64(party[2]);
        var bestScore = Math.Max(previousScore, score);
        var receivedScore = Convert.ToInt64(party[4]);
        var milestones = LessonRules.Milestones[session.BaseId].Where(rule => rule.Score > receivedScore && rule.Score <= bestScore).ToArray();
        var rewardScore = milestones.Length == 0 ? receivedScore : milestones.Max(rule => rule.Score);
        var touchedItems = new HashSet<long>();
        foreach (var reward in milestones.SelectMany(rule => rule.Rewards))
        {
            if (reward.Type == 1)
            {
                await UpsertItemQuantityAsync(connection, transaction, userId, reward.MasterId, reward.Quantity);
                touchedItems.Add(reward.MasterId);
            }
            else if (reward.Type is 12 or 13)
            {
                var column = reward.Type == 12 ? "coin" : "free_jewel";
                await ExecuteAsync(connection, transaction, $"update user_game_states set {column} = {column} + $2, updated_at = now() where \"userId\" = $1", userId, reward.Quantity);
                await ExecuteAsync(connection, transaction, $"update user_item_currencies set {column} = {column} + $2 where \"userId\" = $1", userId, reward.Quantity);
            }
            else throw new InvalidOperationException($"稽古奖励类型尚未实现：{reward.Type}");
        }
        await ExecuteAsync(connection, transaction,
            """
            update user_live_lesson_party_states
            set payload = payload || jsonb_build_object('bestScore',$3,'rewardReceivedHighScore',$4), updated_at=now()
            where "userId"=$1 and lesson_master_id=$2
            """, userId, session.BaseId, bestScore, rewardScore);
        party[2] = bestScore;
        party[4] = rewardScore;
        var present = new List<object?>
        {
            DataObject(39, party), DataObject(109, await ReadDailyLimitAsync(connection, userId, transaction)),
            DataObject(0, await ReadUserAsync(connection, userId)), DataObject(128, await ReadCurrencyAsync(connection, userId))
        };
        foreach (var item in await ReadItemPossessionsAsync(connection, userId))
            if (touchedItems.Contains(Convert.ToInt64(item[1]))) present.Add(DataObject(27, item));
        var result = await BuildLessonFinishResultAsync(connection, userId, session.BaseId, score, judges,
            previousScore, bestScore, milestones.SelectMany(rule => rule.Rewards).ToArray());
        var settlement = new LiveSettlement(result, present.ToArray());
        await ExecuteAsync(connection, transaction, "update user_lesson_sessions set completion=$2, finish_hash=$3 where \"userId\"=$1",
            userId, MsgPack.Encode(new object?[] { settlement.Result, settlement.Present }), hash);
        return settlement;
    }
    async Task<object?[]> BuildLessonRateResultAsync(NpgsqlConnection connection, long userId, decimal achievement)
    {
        var rating = await ReadPlayerRatingAsync(connection, userId);
        // 稽古不改写普通演出的 Rating，但结果页仍需要本次达成率的 Before/After。
        return new object?[] { new object?[] { 0d, (double)achievement }, new object?[] { 0d, 0d }, rating, rating };
    }

    async Task<object?[]> BuildLessonFinishResultAsync(NpgsqlConnection connection, long userId, long baseId, long score,
        Dictionary<int, int> judges, long before, long after, LessonRules.Reward[] rewards)
    {
        var characterBase = (await ReadCharacterBasesAsync(connection, userId)).Single(row => Convert.ToInt64(row[1]) == baseId);
        var achievement = PlayerProgressionRules.Achievement(judges);
        var result = new object?[31];
        result[1] = Array.Empty<object?>();
        result[2] = await BuildLessonRateResultAsync(connection, userId, achievement);
        result[5] = judges.Where(pair => pair.Key < 5).Sum(pair => pair.Value) == 0 ? 6 : 1;
        result[6] = PlayerProgressionRules.Grade(achievement);
        result[8] = score > before;
        result[9] = 0; result[10] = 0;
        result[11] = Array.Empty<object?>(); result[12] = Array.Empty<object?>();
        result[13] = (double)achievement; result[20] = 0; result[21] = 0;
        result[23] = Array.Empty<object?>(); result[30] = Array.Empty<object?>();
        // 尚未实现 SP 加成计算，返回现有等级和点数，避免伪造角色成长。
        result[24] = new object?[] { baseId,
            new object?[] { characterBase[2], characterBase[2], characterBase[3], characterBase[3], 0, Array.Empty<object?>() },
            rewards.Select(reward => (object?)new object?[] { reward.Type, reward.MasterId, reward.Quantity, null, null, null, false }).ToArray(), before, after };
        return result;
    }
}
