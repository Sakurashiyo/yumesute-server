using Npgsql;

sealed partial class UserDataService
{
    // 玩家状态行锁由调用方持有，解锁、扣票和结算共用同一事务，避免重试重复消费。
    static async Task<List<object?[]>> SynchronizeMusicUnlocksAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        var musics = MusicUnlockRules.Musics.Values.ToArray();
        await ExecuteAsync(connection, transaction, """
            insert into user_live_music_states(id,"userId",music_master_id,is_unlocked)
            select seed.id,$1,seed.music_id,seed.owned
            from unnest($2::bigint[],$3::bigint[],$4::boolean[]) seed(id,music_id,owned)
            on conflict ("userId",music_master_id) do nothing
            """, userId, musics.Select(music => UserScopedId(userId,music.Id)).ToArray(),
            musics.Select(music => music.Id).ToArray(), musics.Select(music => music.OwnershipCondition == 1).ToArray());
        var rank = await ReadRankStateAsync(connection, transaction, userId);
        var rows = await ReadLiveMusicStatesAsync(connection, userId);
        var olivierCount = rows.Count(row => Convert.ToInt32(row[8]) == 3);
        var maxClearedLevel = Convert.ToInt32((await ReadLiveAchievementAsync(connection,userId))[2]);
        var changed = new List<object?[]>();
        foreach (var row in rows)
        {
            if (!MusicUnlockRules.Musics.TryGetValue(Convert.ToInt64(row[2]),out var music)) continue;
            var owned = (bool)row[9]! || music.OwnershipCondition == 1;
            var stella = MusicUnlockRules.StellaReleased(music,owned,(bool)row[5]!,rank.Rank,olivierCount);
            var status = MusicUnlockRules.OlivierStatus(music,owned,stella,Convert.ToInt32(row[8]),maxClearedLevel);
            if (Equals(row[5],stella) && Equals(row[8],status) && Equals(row[9],owned)) continue;
            row[5]=stella; row[8]=status; row[9]=owned; changed.Add(row);
        }
        if (changed.Count>0)
            await ExecuteAsync(connection,transaction,"""
                update user_live_music_states state set stella_released=change.stella,
                  olivier_release_status=change.status,is_unlocked=change.owned,updated_at=now()
                from unnest($2::bigint[],$3::boolean[],$4::integer[],$5::boolean[]) change(music_id,stella,status,owned)
                where state."userId"=$1 and state.music_master_id=change.music_id
                """,userId,changed.Select(row=>Convert.ToInt64(row[2])).ToArray(),
                changed.Select(row=>(bool)row[5]!).ToArray(),changed.Select(row=>Convert.ToInt32(row[8])).ToArray(),
                changed.Select(row=>(bool)row[9]!).ToArray());
        return changed;
    }

    static async Task<object?[]> ReadLiveAchievementAsync(NpgsqlConnection connection,long userId)
    {
        var maxClearedLevel = 0;
        await using (var command = new NpgsqlCommand("""
            select live_master_id from user_live_play_results
            where "userId"=$1 and difficulty=5 and clear_count>0
            """, connection))
        {
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!PlayerProgressionRules.Charts.TryGetValue(reader.GetInt64(0),out var chart))
                    throw new InvalidOperationException("OLIVIER 通关记录缺少匹配主数据");
                maxClearedLevel = Math.Max(maxClearedLevel,chart.Level);
            }
        }
        var count = (await ReadLiveMusicStatesAsync(connection,userId)).Count(row=>Convert.ToInt32(row[8])==3);
        return new object?[]{userId,count,maxClearedLevel};
    }

    static async Task ValidateMusicDifficultyAsync(NpgsqlConnection connection, long userId, PlayerProgressionRules.Chart chart, bool auto)
    {
        var row = (await ReadLiveMusicStatesAsync(connection,userId)).SingleOrDefault(row=>Convert.ToInt64(row[2])==chart.MusicMasterId)
            ?? throw new InvalidOperationException("缺少歌曲解锁状态");
        if (!(bool)row[9]!) throw new BadHttpRequestException(MusicUnlockErrors.NotOwned,409);
        if (chart.Difficulty==4 && !(bool)row[5]! || chart.Difficulty==5 &&
            (Convert.ToInt32(row[8])!=3 && (auto || Convert.ToInt32(row[8])!=1)))
            throw new BadHttpRequestException(MusicUnlockErrors.Locked,409);
    }

    public async Task<object?[]> GetMusicPresentDataAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException("ACCOUNT_UNAUTHORIZED",401);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        return (await ReadLiveMusicStatesAsync(connection,userId)).Select(row=>DataObject(25,row))
            .Append(DataObject(130,await ReadLiveAchievementAsync(connection,userId))).ToArray();
    }

    static async Task RecordDifficultyUnlockAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,long userId,
        PlayerProgressionRules.Chart chart,object?[] finish,bool auto,bool cleared,decimal achievement)
    {
        if (auto || !cleared || !FinishLifeIsPositive(finish)) return;
        // 协力或活动提供的未拥有歌曲不写入个人 STELLA / 挑战解锁。
        if (!(bool)(await ReadLiveMusicStatesAsync(connection,userId)).Single(row=>Convert.ToInt64(row[2])==chart.MusicMasterId)[9]!) return;
        var music = MusicUnlockRules.Musics[chart.MusicMasterId];
        if (chart.Difficulty==3 && music.StellaCondition==13 &&
            ReadFinishJudges(finish).Where(pair=>pair.Key<=3).Sum(pair=>(long)pair.Value)<=music.StellaValue)
            await ExecuteAsync(connection,transaction,"""
                update user_live_music_states set stella_released=true,updated_at=now()
                where "userId"=$1 and music_master_id=$2
                """,userId,chart.MusicMasterId);
        if (chart.Difficulty==4 && achievement>=100m)
            await ExecuteAsync(connection,transaction,"""
                update user_live_music_states set olivier_release_status=1,updated_at=now()
                where "userId"=$1 and music_master_id=$2 and olivier_release_status=0
                """,userId,chart.MusicMasterId);
    }

    public async Task<object?[]> ExchangeMusicAsync(HttpContext context,long id,bool score)
    {
        PlayerProgressionRules.Chart? chart = null;
        if (score && (!PlayerProgressionRules.Charts.TryGetValue(id,out chart) || chart.Difficulty!=5 || chart.UnlockCondition!=14))
            throw new BadHttpRequestException(MusicUnlockErrors.InvalidRequest);
        var musicId = score ? chart!.MusicMasterId : id;
        if (!MusicUnlockRules.Musics.TryGetValue(musicId,out var music) || !score && music.OwnershipCondition!=10)
            throw new BadHttpRequestException(MusicUnlockErrors.InvalidRequest);
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException("ACCOUNT_UNAUTHORIZED",401);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection,transaction,userId);
        await SynchronizeMusicUnlocksAsync(connection,transaction,userId);
        var row = (await ReadLiveMusicStatesAsync(connection,userId)).Single(row=>Convert.ToInt64(row[2])==musicId);
        var already = score ? Convert.ToInt32(row[8])==3 : (bool)row[9]!;
        if (!already)
        {
            if (score && (!(bool)row[9]! || !(bool)row[5]! || Convert.ToInt32(row[8])!=2))
                throw new BadHttpRequestException(MusicUnlockErrors.NotPurchasable,409);
            if (await ExecuteMarketItemCostAsync(connection,transaction,userId,MusicUnlockRules.TicketId,score?1:10)!=1)
                throw new BadHttpRequestException(MusicUnlockErrors.InsufficientTickets,409);
            await ExecuteAsync(connection,transaction,score ? """
                update user_live_music_states set olivier_release_status=3,updated_at=now()
                where "userId"=$1 and music_master_id=$2
                """ : """
                update user_live_music_states set is_unlocked=true,unlocked_at=now(),updated_at=now()
                where "userId"=$1 and music_master_id=$2
                """,userId,musicId);
        }
        await SynchronizeMusicUnlocksAsync(connection,transaction,userId);
        var present = (await ReadLiveMusicStatesAsync(connection,userId)).Select(row=>DataObject(25,row)).ToList();
        present.AddRange((await ReadItemPossessionsAsync(connection,userId))
            .Where(row=>Convert.ToInt64(row[1])==MusicUnlockRules.TicketId).Select(row=>DataObject(27,row)));
        present.Add(DataObject(130,await ReadLiveAchievementAsync(connection,userId)));
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "歌曲兑换成功 operation={Operation} userId={UserId} musicId={MusicId} chartId={ChartId} repeated={Repeated} outcome=success",
            score?"music-score-exchange":"music-exchange",userId,musicId,score?id:0,already);
        return present.ToArray();
    }
}