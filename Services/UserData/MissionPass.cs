using Npgsql;

sealed partial class UserDataService
{
    public sealed record MissionPassResponse(object?[] Result, object?[] Present);
    sealed record MissionPassState(int Free, int Sp, int FreeLoops, int SpLoops);

    static Task EnsureMissionPassAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId) =>
        ExecuteAsync(connection, transaction, """
            insert into user_mission_passes("userId",mission_pass_master_id) values($1,$2)
            on conflict ("userId",mission_pass_master_id) do nothing
            """, userId, MissionPassRules.Id);

    static async Task<MissionPassState> ReadMissionPassAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand("""
            select free_phase,sp_phase,free_loop_rewards,sp_loop_rewards from user_mission_passes
            where "userId"=$1 and mission_pass_master_id=$2
            """, connection);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(MissionPassRules.Id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Mission Pass 存档未初始化");
        return new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    static object?[] MissionPassData(long userId, MissionPassState state, MissionPassSettings settings) =>
        [UserScopedId(userId, 9701), userId, MissionPassRules.Id, settings.SpEnabled, state.Free, state.Sp, !settings.IsOpen(DateTimeOffset.UtcNow),
            state.FreeLoops / 4 + 1, state.FreeLoops % 4, state.SpLoops / 4 + 1, state.SpLoops % 4];

    static async Task<int> ReadMissionPointsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand("""
            select quantity from user_item_possessions where "userId"=$1 and item_master_id=$2
            """, connection);
        command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(MissionPassRules.PointItemId);
        var value = await command.ExecuteScalarAsync();
        return value is null ? 0 : Convert.ToInt32(value);
    }

    public async Task<MissionPassResponse> ReceiveMissionPassAsync(HttpContext context, long passId)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        if (passId != MissionPassRules.Id)
            throw new BadHttpRequestException(MissionPassErrors.Unavailable, 404);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var settings = (await GameOperationsService.ReadAsync(connection, transaction, lockForUse: true)).Configuration.MissionPass;
        if (!settings.IsOpen(DateTimeOffset.UtcNow)) throw new BadHttpRequestException(MissionPassErrors.Unavailable, 404);
        // 与演出结算、兑换和其他发奖共用玩家锁，领取阶段与库存写入在同一事务内提交。
        await LockLessonUserAsync(connection, transaction, userId);
        var state = await ReadMissionPassAsync(connection, userId);
        var points = await ReadMissionPointsAsync(connection, userId);
        var reached = settings.Stages.Count(stage => stage.Points <= points);
        var loops = MissionPassRules.ReachedLoops(points);
        var pending = new List<MissionPassRules.Reward>();
        foreach (var stage in settings.Stages.Where(stage => stage.Phase <= reached))
            pending.AddRange(stage.Rewards.Where(reward => (!reward.Sp || settings.SpEnabled)
                && stage.Phase > (reward.Sp ? state.Sp : state.Free)));
        for (var index = Math.Min(state.FreeLoops, state.SpLoops); index < loops; index++)
            pending.AddRange(settings.LoopRewards[index % 4]
                .Where(reward => (!reward.Sp || settings.SpEnabled) && index >= (reward.Sp ? state.SpLoops : state.FreeLoops)));

        var changed = new List<object?>();
        var rewards = new List<object?>();
        foreach (var group in pending.GroupBy(reward => (reward.Type, reward.MasterId)))
        {
            var quantity = checked(group.Sum(reward => reward.Quantity));
            if (group.Key.Type == 3)
                for (var count = 0; count < quantity; count++)
                    rewards.Add(await GrantCollectionEntryAsync(connection, transaction, userId,
                        GachaRules.PosterEntry(group.Key.MasterId), changed));
            else if (group.Key.Type == 1)
            {
                await UpsertItemQuantityAsync(connection, transaction, userId, group.Key.MasterId, quantity);
                rewards.Add(GachaRules.Received(1, group.Key.MasterId, quantity));
            }
            else if (group.Key.Type is 12 or 13)
            {
                var column = group.Key.Type == 12 ? "coin" : "free_jewel";
                await ExecuteAsync(connection, transaction,
                    $"update user_item_currencies set {column}={column}+$2 where \"userId\"=$1", userId, quantity);
                await ExecuteAsync(connection, transaction,
                    $"update user_game_states set {column}={column}+$2,updated_at=now() where \"userId\"=$1", userId, quantity);
                rewards.Add(GachaRules.Received(group.Key.Type, group.Key.MasterId, quantity));
            }
            else throw new InvalidDataException($"Mission Pass 奖励类型未实现：{group.Key.Type}");
        }

        var next = new MissionPassState(Math.Max(state.Free, reached), settings.SpEnabled ? Math.Max(state.Sp, reached) : state.Sp,
            Math.Max(state.FreeLoops, loops), settings.SpEnabled ? Math.Max(state.SpLoops, loops) : state.SpLoops);
        await ExecuteAsync(connection, transaction, """
            update user_mission_passes set free_phase=$3,sp_phase=$4,free_loop_rewards=$5,
                sp_loop_rewards=$6,updated_at=now() where "userId"=$1 and mission_pass_master_id=$2
            """, userId, passId, next.Free, next.Sp, next.FreeLoops, next.SpLoops);
        var present = await MissionPassPresentAsync(connection, userId, next, settings, changed);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "Mission Pass 领奖完成 operation={Operation} userId={UserId} passId={PassId} points={Points} phase={Phase} loopRewards={LoopRewards} rewardCount={RewardCount}",
            "mission-pass-claim", userId, passId, points, next.Free, next.FreeLoops, pending.Count);
        return new([rewards.ToArray(), pending.Count > 0 ? MissionPassRules.Success :
            reached == 0 ? MissionPassRules.NotReached : MissionPassRules.Received], present);
    }

    public async Task<MissionPassResponse> ExchangeMissionPointAsync(HttpContext context, int unit)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        if (unit < 1 || unit > MissionPassRules.MaxPoints / MissionPassRules.ExchangePointPerUnit)
            throw new BadHttpRequestException(MissionPassErrors.Invalid, 400);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var settings = (await GameOperationsService.ReadAsync(connection, transaction, lockForUse: true)).Configuration.MissionPass;
        if (!settings.IsOpen(DateTimeOffset.UtcNow)) throw new BadHttpRequestException(MissionPassErrors.Unavailable, 404);
        await LockLessonUserAsync(connection, transaction, userId);
        var points = checked(unit * MissionPassRules.ExchangePointPerUnit);
        if ((long)await ReadMissionPointsAsync(connection, userId) + points > MissionPassRules.MaxPoints)
            throw new BadHttpRequestException(MissionPassErrors.PointLimit, 409);
        var currency = await ReadCurrencyAsync(connection, userId);
        var free = Convert.ToInt32(currency[2]); var paid = Convert.ToInt32(currency[3]);
        var cost = checked(unit * MissionPassRules.ExchangeJewelPerUnit);
        if ((long)free + paid < cost) throw new BadHttpRequestException(MissionPassErrors.Insufficient, 409);
        var freeCost = Math.Min(free, cost); free -= freeCost; paid -= cost - freeCost;
        await ExecuteAsync(connection, transaction,
            "update user_item_currencies set free_jewel=$2,paid_jewel=$3 where \"userId\"=$1", userId, free, paid);
        await ExecuteAsync(connection, transaction,
            "update user_game_states set free_jewel=$2,paid_jewel=$3,updated_at=now() where \"userId\"=$1", userId, free, paid);
        await UpsertItemQuantityAsync(connection, transaction, userId, MissionPassRules.PointItemId, points);
        var present = await MissionPassPresentAsync(connection, userId, await ReadMissionPassAsync(connection, userId), settings, []);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "Mission Pass 积分兑换完成 operation={Operation} userId={UserId} passId={PassId} units={Units} points={Points} jewelCost={Cost}",
            "mission-pass-exchange", userId, MissionPassRules.Id, unit, points, cost);
        return new([true], present);
    }

    async Task<object?[]> MissionPassPresentAsync(NpgsqlConnection connection, long userId,
        MissionPassState state, MissionPassSettings settings, List<object?> changed)
    {
        var definitions = settings.Stages.SelectMany(stage => stage.Rewards)
            .Concat(settings.LoopRewards.SelectMany(rewards => rewards)).ToArray();
        var items = definitions.Where(reward => reward.Type == 1).Select(reward => reward.MasterId)
            .Append(MissionPassRules.PointItemId).Append(GachaRules.DuplicateItemId).ToHashSet();
        var posters = definitions.Where(reward => reward.Type == 3).Select(reward => reward.MasterId).ToHashSet();
        changed.Add(DataObject(97, MissionPassData(userId, state, settings)));
        changed.Add(DataObject(0, await ReadUserAsync(connection, userId)));
        changed.Add(DataObject(128, await ReadCurrencyAsync(connection, userId)));
        changed.AddRange((await ReadItemPossessionsAsync(connection, userId))
            .Where(row => items.Contains(Convert.ToInt64(row[1]))).Select(row => DataObject(27, row)));
        changed.AddRange((await ReadPermanentMarketDataAsync(connection, userId)).Cast<object?[]>()
            .Where(row => Convert.ToInt32(row[0]) == PosterDataObjectUnionKey
                && posters.Contains(Convert.ToInt64(((object?[])row[1]!)[1]))));
        return changed.Cast<object?[]>().GroupBy(row => (Convert.ToInt32(row[0]), Convert.ToInt64(((object?[])row[1]!)[0])))
            .Select(group => (object?)group.Last()).ToArray();
    }
}
