using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> LevelUpPosterAsync(HttpContext context, long posterId, int levelTo)
    {
        if (posterId <= 0 || levelTo < 1) throw new BadHttpRequestException(PosterProgressionErrors.Invalid, 400);
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException(AccountErrors.Unauthorized, 401);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var poster = (await ReadPermanentMarketDataAsync(connection, userId)).Cast<object?[]>()
            .Where(row => Convert.ToInt32(row[0]) == PosterDataObjectUnionKey).Select(row => (object?[])row[1]!)
            .SingleOrDefault(row => Convert.ToInt64(row[0]) == posterId)
            ?? throw new BadHttpRequestException(PosterProgressionErrors.NotOwned, 404);
        var master = PosterProgressionRules.Posters[Convert.ToInt64(poster[1])];
        var before = Convert.ToInt32(poster[2]);
        var phase = Convert.ToInt32(poster[3]);
        if (phase < 0 || phase > master.MaxPhase || before < 1 || before > PosterProgressionRules.LevelLimit(master, phase))
            throw new InvalidOperationException("海报等级或突破存档不合法");
        if (levelTo > PosterProgressionRules.LevelLimit(master, phase))
            throw new BadHttpRequestException(PosterProgressionErrors.LevelLimit, 409);
        // 目标等级重试或乱序到达时，保留已达到的等级，避免重复扣除素材。
        var costs = PosterProgressionRules.LevelCosts(master, before, levelTo);
        foreach (var (item, quantity) in costs.OrderBy(pair => pair.Key))
            if (await ExecuteMarketItemCostAsync(connection, transaction, userId, item, quantity) != 1)
                throw new BadHttpRequestException(PosterProgressionErrors.InsufficientItems, 409);
        if (levelTo > before)
        {
            poster[2] = levelTo;
            await SavePermanentMarketDataAsync(connection, transaction, userId, LegacyPosterStorageKey, master.Id.ToString(), poster);
        }
        var present = new List<object?> { DataObject(PosterDataObjectUnionKey, poster) };
        present.AddRange((await ReadItemPossessionsAsync(connection, userId))
            .Where(row => costs.ContainsKey(Convert.ToInt64(row[1]))).Select(row => DataObject(27, row)));
        present.AddRange(await GrantPosterCostumesAsync(connection, transaction, userId, master.Id, phase));
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "海报升级完成 operation=poster-level-up userId={UserId} posterId={PosterId} masterId={MasterId} before={Before} after={After} outcome=success",
            userId, posterId, master.Id, before, poster[2]);
        return present.ToArray();
    }

    static async Task<object?[]> GrantPosterCostumesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long userId, long posterMasterId, int phase)
    {
        var rewards = PosterProgressionRules.Posters[posterMasterId].Costumes.Where(costume => costume.Phase <= phase);
        var changed = new List<object?>();
        foreach (var reward in rewards)
        {
            await using var insert = new NpgsqlCommand("""
                insert into user_character_costumes(id,"userId",costume_master_id) values($1,$2,$3)
                on conflict ("userId",costume_master_id) do nothing returning id
                """, connection, transaction);
            insert.Parameters.AddWithValue(CardInventoryWriter.NewId());
            insert.Parameters.AddWithValue(userId);
            insert.Parameters.AddWithValue(reward.Id);
            if (await insert.ExecuteScalarAsync() is long id)
                changed.Add(DataObject(43, new object?[] { id, reward.Id }));
        }
        return changed.ToArray();
    }

    static async Task<int> SynchronizePosterCostumesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        var changed = 0;
        foreach (var entry in (await ReadPermanentMarketDataAsync(connection, userId)).Cast<object?[]>()
            .Where(row => Convert.ToInt32(row[0]) == PosterDataObjectUnionKey))
        {
            var poster = (object?[])entry[1]!;
            changed += (await GrantPosterCostumesAsync(connection, transaction, userId,
                Convert.ToInt64(poster[1]), Convert.ToInt32(poster[3]))).Length;
        }
        return changed;
    }
}
