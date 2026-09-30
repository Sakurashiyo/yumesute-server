using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    static readonly Lazy<IReadOnlyDictionary<long, MissionClaimDefinition>> MissionClaimDefinitions = new(LoadMissionClaimDefinitions);

    public sealed record MissionClaimResponse(object?[] Rewards, object?[] Present);

    public async Task<object?[]> MarkShopFreePackMissionAsync(HttpContext context, object? payload)
    {
        if (payload is not object?[] { Length: > 0 } values || GetLong(values, 0) != 4101)
            return Array.Empty<object?>();
        var userId = await GetCurrentUserIdAsync(context)
            ?? throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into user_mission_statuses
              (id, "userId", is_cleared, is_received, progress, completed_at, mission_from, mission_to)
            values ($1, $2, true, false, $3, now(), $4, $5)
            on conflict ("userId", mission_from, mission_to)
            do update set is_cleared = true, progress = excluded.progress,
                          completed_at = coalesce(user_mission_statuses.completed_at, now()),
                          updated_at = now()
            """, connection);
        foreach (var (from, to, progress) in new[] { (100400L, 100401L, 1), (100300L, 100301L, 3) })
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue(UserScopedId(userId, 1000000 + from));
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(progress);
            command.Parameters.AddWithValue(from);
            command.Parameters.AddWithValue(to);
            await command.ExecuteNonQueryAsync();
        }
        var present = (await ReadMissionsAsync(connection, userId))
            .Where(row => Convert.ToInt64(row[5]) is 100300 or 100400)
            .Select(row => (object?)DataObject(48, row))
            .ToList();
        present.Add(DataObject(0, await ReadUserAsync(connection, userId)));
        present.Add(DataObject(128, await ReadCurrencyAsync(connection, userId)));
        return present.ToArray();
    }

    public async Task<MissionClaimResponse> ReceiveMissionRewardsAsync(HttpContext context, long? missionId, int? category)
    {
        if (missionId is null && category is null)
            throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        var userId = await GetCurrentUserIdAsync(context)
            ?? throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var candidates = new List<(long Id, long From, long To, int Progress, DateTime? CompletedAt)>();
        await using (var command = new NpgsqlCommand(
            """
            select id, mission_from, mission_to, progress, completed_at
            from user_mission_statuses
            where "userId" = $1 and is_cleared and not is_received
            for update
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                candidates.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetDateTime(4)));
        }

        var selected = candidates.Where(row =>
                MissionClaimDefinitions.Value.TryGetValue(row.From, out var definition)
                && (missionId == row.From || category == definition.Category))
            .ToArray();
        var rewards = new List<object?>();
        var present = new List<object?>();
        var updatedItems = new HashSet<long>();
        var currencyChanged = false;
        foreach (var status in selected)
        {
            var definition = MissionClaimDefinitions.Value[status.From];
            var stage = definition.Stages.SingleOrDefault(value => value.Id == status.To);
            if (stage is null) throw new InvalidOperationException($"任务 {status.From} 的阶段 {status.To} 不存在");
            foreach (var reward in stage.Rewards)
            {
                rewards.Add(new object?[] { reward.Type, reward.MasterId, reward.Quantity, null, null, null, false });
                if (reward.Type == 1)
                {
                    await UpsertItemQuantityAsync(connection, transaction, userId, reward.MasterId, reward.Quantity);
                    updatedItems.Add(reward.MasterId);
                }
                else if (reward.Type is 12 or 13)
                {
                    var column = reward.Type == 12 ? "coin" : "free_jewel";
                    await ExecuteAsync(connection, transaction,
                        $"update user_item_currencies set {column} = {column} + $2 where \"userId\" = $1",
                        userId, reward.Quantity);
                    await ExecuteAsync(connection, transaction,
                        $"update user_game_states set {column} = {column} + $2 where \"userId\" = $1",
                        userId, reward.Quantity);
                    currencyChanged = true;
                }
                else
                {
                    throw new InvalidOperationException($"任务 {status.From} 使用尚未实现的奖励类型 {reward.Type}");
                }
            }

            await ExecuteAsync(connection, transaction,
                "update user_mission_statuses set is_received = true, updated_at = now() where id = $1 and \"userId\" = $2",
                status.Id, userId);
            present.Add(DataObject(48, new object?[]
            {
                status.Id, true, true, status.Progress, status.CompletedAt, status.From, status.To
            }));
        }

        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "Mission rewards claimed userId={UserId} missionId={MissionId} category={Category} claimedCount={ClaimedCount} rewardCount={RewardCount}",
            userId, missionId, category, selected.Length, rewards.Count);
        if (currencyChanged)
        {
            present.Add(DataObject(0, await ReadUserAsync(connection, userId)));
            present.Add(DataObject(128, await ReadCurrencyAsync(connection, userId)));
        }
        if (updatedItems.Count > 0)
        {
            foreach (var item in await ReadItemPossessionsAsync(connection, userId))
                if (updatedItems.Contains(Convert.ToInt64(item[1]))) present.Add(DataObject(27, item));
        }
        return new MissionClaimResponse(rewards.ToArray(), present.ToArray());
    }

    static IReadOnlyDictionary<long, MissionClaimDefinition> LoadMissionClaimDefinitions()
    {
        using var stream = typeof(UserDataService).Assembly.GetManifestResourceStream("Mission.MissionMaster.json")
            ?? throw new InvalidOperationException("缺少任务主数据");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().ToDictionary(
            row => row[0].GetInt64(),
            row => new MissionClaimDefinition(
                row[1].GetInt32(),
                row[16].EnumerateArray().Select(stage => new MissionClaimStage(
                    stage[0].GetInt64(),
                    stage[4].EnumerateArray().Select(reward => new MissionClaimReward(
                        reward[1].GetInt32(), reward[0].GetInt64(), reward[2].GetInt32())).ToArray()))
                    .ToArray()));
    }

    sealed record MissionClaimDefinition(int Category, MissionClaimStage[] Stages);
    sealed record MissionClaimStage(long Id, MissionClaimReward[] Rewards);
    sealed record MissionClaimReward(int Type, long MasterId, int Quantity);
}
