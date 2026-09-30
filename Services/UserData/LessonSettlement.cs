using System.Security.Cryptography;
using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> FinishLessonAsync(HttpContext context, object? payload)
    {
        if (payload is not object?[] { Length: 3 } finish || GetLong(finish, 0) is not long score || score < 0
            || GetLong(finish, 1) is not long combo || combo is < 0 or > int.MaxValue)
            throw new BadHttpRequestException(LessonErrors.InvalidFinish, 400);
        var judges = ReadFinishJudges(finish);
        PlayerProgressionRules.Achievement(judges);
        if (combo > judges.Values.Sum(count => (long)count)) throw new BadHttpRequestException(LessonErrors.InvalidFinish, 400);
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException(PartyNotFound, 404);
        var hash = SHA256.HashData(MsgPack.Encode(payload));
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var session = await ReadLessonSessionAsync(connection, transaction, userId)
            ?? throw new BadHttpRequestException(LessonErrors.SessionMissing, 409);
        if (session.Completion is not null)
        {
            if (!hash.SequenceEqual(session.FinishHash!)) throw new BadHttpRequestException(LessonErrors.SessionConflict, 409);
            return (object?[])MsgPack.Decode(session.Completion)!;
        }
        var party = (await ReadLessonPartiesAsync(connection, userId, transaction)).Single(row => Convert.ToInt64(row[0]) == session.BaseId);
        var bestScore = Math.Max(Convert.ToInt64(party[2]), score);
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
        var completion = present.ToArray();
        await ExecuteAsync(connection, transaction, "update user_lesson_sessions set completion=$2, finish_hash=$3 where \"userId\"=$1", userId, MsgPack.Encode(completion), hash);
        await transaction.CommitAsync();
        return completion;
    }
}
