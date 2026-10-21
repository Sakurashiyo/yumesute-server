using Npgsql;

sealed partial class UserDataService
{
    static AuditionRules.Master ValidateAuditionStart(object?[] start, long chartId)
    {
        if (GetLong(start, 2) is not long id)
            throw new BadHttpRequestException(AuditionErrors.InvalidStart);
        var master = AuditionRules.Get(id);
        var chart = PlayerProgressionRules.Charts[chartId];
        if (chart.MusicMasterId != master.MusicId || chart.Difficulty is < 1 or > 4
            || master.Start > DateTime.UtcNow || master.End <= DateTime.UtcNow
            || new[] { 3, 9, 10, 11, 13, 14 }.Any(index => start.ElementAtOrDefault(index) is not null)
            || start.ElementAtOrDefault(8) is true)
            throw new BadHttpRequestException(AuditionErrors.InvalidStart);
        return master;
    }

    static int CountAuditionStarActs(object?[] finish, object?[] unit)
    {
        if (finish.ElementAtOrDefault(6) is null) return 0;
        if (finish[6] is not object?[] blocks)
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidFinish);
        var timings = (Dictionary<int, object?>)unit[1]!;
        var seen = new HashSet<int>();
        foreach (var value in blocks)
        {
            if (value is not object?[] { Length: 5 } row
                || GetLong(row, 3) is not long second || second is < 0 or > int.MaxValue
                || !seen.Add((int)second) || !timings.TryGetValue((int)second, out var timing)
                || ((object?[])timing!)[2] is not true
                || GetLong(row, 1) is not >= 0 || GetLong(row, 2) is not (>= 0 and <= int.MaxValue)
                || GetLong(row, 4) is not (>= 0 and <= int.MaxValue))
                throw new BadHttpRequestException(LiveProgressionErrors.InvalidFinish);
        }
        return seen.Count;
    }

    async Task FinishAuditionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId,
        object?[] modeContext, LiveRewardRules.Snapshot live, object?[] finish, long score, bool auto,
        bool cleared, decimal achievement, object?[] result, List<object?> present)
    {
        if (modeContext is not { Length: 3 } || GetLong(modeContext, 0) != 1
            || GetLong(modeContext, 1) is not long auditionId || modeContext[2] is not object?[] { Length: 11 } party
            || live.Unit is null)
            throw new InvalidDataException("公演结算缺少开演队伍快照");
        _ = AuditionRules.Get(auditionId);
        var previous = (await ReadAuditionClearsAsync(connection, userId))
            .SingleOrDefault(row => Convert.ToInt64(row[1]) == auditionId);
        var before = previous is null ? 0 : Convert.ToInt32(previous[2]);
        var starActs = CountAuditionStarActs(finish, live.Unit);
        var achieved = !auto && cleared ? AuditionRules.ClearedPhase(auditionId, score, starActs) : 0;
        var after = Math.Max(before, achieved);
        result[9] = before;
        result[10] = after;
        result[14] = auditionId;
        var received = new List<object?>();
        var touchedItems = new HashSet<long>();
        if (!auto)
        {
            var oldBest = await ReadAuditionHighScorePartyAsync(connection, userId, auditionId);
            result[8] = oldBest is null || score > Convert.ToInt64(oldBest[1]);
            // phase=0 保存本人最高分，其余行分别冻结达到该阶段的最高分队伍。
            for (var phase = 0; phase <= achieved; phase++)
            {
                var snapshot = (object?[])party.Clone();
                snapshot[5] = PlayerProgressionRules.Grade(achievement);
                await ExecuteAsync(connection, transaction, """
                    insert into user_audition_parties("userId",audition_master_id,phase,score,snapshot)
                    values($1,$2,$3,$4,$5)
                    on conflict("userId",audition_master_id,phase) do update
                      set score=excluded.score,snapshot=excluded.snapshot
                      where user_audition_parties.score<excluded.score
                    """, userId, auditionId, phase, score, MsgPack.Encode(snapshot));
            }
            if (after > before)
            {
                foreach (var reward in AuditionRules.Phases[auditionId]
                    .Where(phase => phase.Number > before && phase.Number <= after)
                    .SelectMany(phase => AuditionRules.Rewards[phase.RewardPackageId]))
                {
                    if (reward.Type == 1)
                    {
                        await UpsertItemQuantityAsync(connection, transaction, userId, reward.MasterId, reward.Quantity);
                        touchedItems.Add(reward.MasterId);
                    }
                    else if (reward.Type == 13)
                        await AddEpisodeJewelsAsync(connection, transaction, userId, reward.Quantity);
                    else throw new InvalidDataException($"未支持的公演奖励类型：{reward.Type}");
                    received.Add(new object?[] { reward.Type, reward.MasterId, reward.Quantity, null, null, null, false });
                }
                await ExecuteAsync(connection, transaction, """
                    insert into user_audition_clears("userId",audition_master_id,clear_phase) values($1,$2,$3)
                    on conflict("userId",audition_master_id) do update set clear_phase=excluded.clear_phase
                    """, userId, auditionId, after);
            }
        }
        result[11] = received.ToArray();
        if (after > 0)
        {
            var progress = (await ReadAuditionClearsAsync(connection, userId))
                .Single(row => Convert.ToInt64(row[1]) == auditionId);
            present.Add(DataObject(90, progress));
        }
        foreach (var item in await ReadItemPossessionsAsync(connection, userId))
            if (touchedItems.Contains(Convert.ToInt64(item[1]))) present.Add(DataObject(27, item));
    }
}
