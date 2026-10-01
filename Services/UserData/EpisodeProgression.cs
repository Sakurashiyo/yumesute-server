using Npgsql;

sealed partial class UserDataService
{
    static async Task<bool?> ReadEpisodeFlagAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long userId, long episodeId)
    {
        await using var command = new NpgsqlCommand(
            "select has_read_all from user_episode_read_states where \"userId\"=$1 and episode_master_id=$2", connection, transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(episodeId);
        return await command.ExecuteScalarAsync() is bool flag ? flag : null;
    }

    static async Task AddEpisodeJewelsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, int quantity)
    {
        await AddFreeJewelsAsync(connection, transaction, userId, quantity);
        await ExecuteAsync(connection, transaction,
            "update user_game_states set free_jewel=free_jewel+$2,updated_at=now() where \"userId\"=$1", userId, quantity);
    }

    async Task<(object?[] Rewards, object?[] PresentData, object?[] Notifications)> CompleteGeneralEpisodeAsync(
        HttpContext context, NpgsqlConnection connection, NpgsqlTransaction transaction,
        long userId, long episodeId, bool hasReadAll, EpisodeProgressionRules.Reward[] packages)
    {
        var present = new List<object?> { DataObject(95, new object?[] { episodeId, hasReadAll }) };
        var rewards = new List<object?>();
        var items = new HashSet<long>();
        var stored = (await ReadPermanentMarketDataAsync(connection, userId)).Cast<object?[]>().ToArray();
        foreach (var reward in packages)
        {
            if (reward.Quantity <= 0) throw new InvalidOperationException("剧情奖励数量必须为正数");
            int? afterPhase = null;
            switch (reward.Type)
            {
                case 1:
                    await UpsertItemQuantityAsync(connection, transaction, userId, reward.MasterId, reward.Quantity);
                    items.Add(reward.MasterId);
                    break;
                case 13:
                    await AddEpisodeJewelsAsync(connection, transaction, userId, reward.Quantity);
                    break;
                case 3:
                    // 主线海报同名重复获得时提升突破阶段，保留原有等级和实例 ID。
                    var poster = stored.Where(row => Convert.ToInt32(row[0]) == PosterDataObjectUnionKey)
                        .Select(row => (object?[])row[1]!).SingleOrDefault(row => Convert.ToInt64(row[1]) == reward.MasterId);
                    poster = poster is null
                        ? new object?[] { UserScopedId(userId, 280000 + reward.MasterId), reward.MasterId, 1, reward.Quantity - 1, 0, 0, false, 0 }
                        : (object?[])poster.Clone();
                    if (stored.Any(row => Convert.ToInt32(row[0]) == PosterDataObjectUnionKey && Convert.ToInt64(((object?[])row[1]!)[1]) == reward.MasterId))
                        poster[3] = checked(Convert.ToInt32(poster[3]) + reward.Quantity);
                    if (!EpisodeProgressionRules.PosterMaxPhases.TryGetValue(reward.MasterId, out var maximum)
                        || Convert.ToInt32(poster[3]) > maximum)
                        throw new InvalidOperationException($"剧情海报超过突破上限，不能丢弃奖励：{reward.MasterId}");
                    afterPhase = Convert.ToInt32(poster[3]);
                    await SavePermanentMarketDataAsync(connection, transaction, userId, LegacyPosterStorageKey, reward.MasterId.ToString(), poster);
                    present.Add(DataObject(PosterDataObjectUnionKey, poster));
                    break;
                case 8:
                    if (reward.Quantity != 1) throw new InvalidOperationException("剧情铭牌奖励数量发生变化");
                    var nameplate = stored.Where(row => Convert.ToInt32(row[0]) == 45)
                        .Select(row => (object?[])row[1]!).SingleOrDefault(row => Convert.ToInt64(row[1]) == reward.MasterId)
                        ?? new object?[] { UserScopedId(userId, 450000 + reward.MasterId), reward.MasterId };
                    await SavePermanentMarketDataAsync(connection, transaction, userId, 45, reward.MasterId.ToString(), nameplate);
                    present.Add(DataObject(45, nameplate));
                    break;
                default:
                    throw new InvalidOperationException($"剧情奖励类型尚未实现：{reward.Type}");
            }
            rewards.Add(new object?[] { reward.Type, reward.MasterId, reward.Quantity, null, null, afterPhase, false });
        }
        if (EpisodeProgressionRules.Characters.TryGetValue(episodeId, out var character))
            present.Add(DataObject(4, await UpdateCharacterSideStoryStateAsync(connection, transaction, userId,
                character.CharacterId, character.Order, character.Order)));
        present.Add(DataObject(0, await ReadUserAsync(connection, userId)));
        present.Add(DataObject(128, await ReadCurrencyAsync(connection, userId)));
        foreach (var item in await ReadItemPossessionsAsync(connection, userId))
            if (items.Contains(Convert.ToInt64(item[1]))) present.Add(DataObject(27, item));
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "剧情阅读完成 operation={Operation} userId={UserId} episodeId={EpisodeId} hasReadAll={HasReadAll}",
            "episode-read", userId, episodeId, hasReadAll);
        return (rewards.ToArray(), present.ToArray(), Array.Empty<object?>());
    }
}
