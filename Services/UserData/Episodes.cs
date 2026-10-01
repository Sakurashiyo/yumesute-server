using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> ReleaseSideStoryAsync(HttpContext context, long characterMasterId, int order)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null) return Array.Empty<object?>();

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var character = await UpdateCharacterSideStoryStateAsync(
            connection,
            transaction,
            userId.Value,
            characterMasterId,
            releasedOrder: Math.Max(order, 1),
            readOrder: null);

        var item = await ReadOrCreateItemPossessionAsync(connection, transaction, userId.Value, 130023);

        await transaction.CommitAsync();
        return new object?[] { DataObject(27, item), DataObject(4, character) };
    }

    public async Task<(object?[] Rewards, object?[] PresentData, object?[] Notifications)> ReadEpisodeAsync(
        HttpContext context, long episodeMasterId, bool hasReadAll = false)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        if (!EpisodeProgressionRules.Rewards.TryGetValue(episodeMasterId, out var rewards))
            throw new BadHttpRequestException(EpisodeErrors.NotFound, 404);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var previous = await ReadEpisodeFlagAsync(connection, transaction, userId, episodeMasterId);
        hasReadAll |= previous == true;
        if (previous is not null)
        {
            await UpsertEpisodeReadStateAsync(connection, transaction, userId, episodeMasterId, hasReadAll);
            await transaction.CommitAsync();
            return (Array.Empty<object?>(), new object?[] { DataObject(95, new object?[] { episodeMasterId, hasReadAll }) }, Array.Empty<object?>());
        }
        if (EpisodeProgressionRules.Previous.TryGetValue(episodeMasterId, out var predecessor) && predecessor is long id
            && await ReadEpisodeFlagAsync(connection, transaction, userId, id) is null)
            throw new BadHttpRequestException(EpisodeErrors.PreviousUnread, 409);
        if (EpisodeProgressionRules.Characters.TryGetValue(episodeMasterId, out var characterEpisode))
        {
            await using var ownership = new NpgsqlCommand(
                "select exists(select 1 from user_character_cards where \"userId\"=$1 and character_master_id=$2)", connection, transaction);
            ownership.Parameters.AddWithValue(userId);
            ownership.Parameters.AddWithValue(characterEpisode.CharacterId);
            if (await ownership.ExecuteScalarAsync() is not true)
                throw new BadHttpRequestException(EpisodeErrors.CharacterNotOwned, 403);
        }
        await UpsertEpisodeReadStateAsync(connection, transaction, userId, episodeMasterId, hasReadAll);
        if (episodeMasterId is not (1043 or 110011 or 1050101))
            return await CompleteGeneralEpisodeAsync(context, connection, transaction, userId, episodeMasterId, hasReadAll, rewards);

        if (episodeMasterId == 110011)
        {
            var character = await UpdateCharacterSideStoryStateAsync(
                connection,
                transaction,
                userId,
                characterMasterId: 110010,
                releasedOrder: 1,
                readOrder: 1);

            await AddEpisodeJewelsAsync(connection, transaction, userId, 30);
            var currency = await ReadCurrencyAsync(connection, transaction, userId);
            var characterResource = await UpsertCharacterResourceProgressAsync(
                connection,
                transaction,
                userId,
                characterBaseMasterId: 101,
                resourceType: 4,
                resourceMasterId: 10204,
                level: 1,
                exp: 1);

            await transaction.CommitAsync();

            return (
                new object?[] { new object?[] { 13, 0, 30, null, null, null, false } },
                new object?[]
                {
                    DataObject(95, new object?[] { episodeMasterId, hasReadAll }),
                    DataObject(4, character),
                    DataObject(128, currency),
                    DataObject(96, characterResource)
                },
                Array.Empty<object?>());
        }

        if (episodeMasterId == 1043)
        {
            await AddEpisodeJewelsAsync(connection, transaction, userId, 10);
            var currency = await ReadCurrencyAsync(connection, transaction, userId);
            var kokonaResource = await UpsertCharacterResourceProgressAsync(
                connection,
                transaction,
                userId,
                characterBaseMasterId: 205,
                resourceType: 10,
                resourceMasterId: 10504,
                level: 1,
                exp: 1);
            var shizukaResource = await UpsertCharacterResourceProgressAsync(
                connection,
                transaction,
                userId,
                characterBaseMasterId: 102,
                resourceType: 10,
                resourceMasterId: 10504,
                level: 1,
                exp: 1);

            await transaction.CommitAsync();

            return (
                new object?[] { new object?[] { 13, 0, 10, null, null, null, false } },
                new object?[]
                {
                    DataObject(95, new object?[] { episodeMasterId, hasReadAll }),
                    DataObject(128, currency),
                    DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 1, null, 2900, 2901 }),
                    DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 1, null, 23, 23 }),
                    DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 2, null, 24, 24 }),
                    DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 13, null, 42, 42 }),
                    DataObject(96, kokonaResource),
                    DataObject(96, shizukaResource)
                },
                new object?[]
                {
                    new object?[] { 0, new object?[] { 23, 23 } }
                });
        }

        await AddEpisodeJewelsAsync(connection, transaction, userId, 50);
        var storyItem = await AddItemPossessionAsync(connection, transaction, userId, 130001, 1);
        var storyCurrency = await ReadCurrencyAsync(connection, transaction, userId);
        await transaction.CommitAsync();

        return (
            new object?[]
            {
                new object?[] { 13, 0, 50, null, null, null, false },
                new object?[] { 1, 130001, 1, null, null, null, false }
            },
            new object?[]
            {
                DataObject(95, new object?[] { episodeMasterId, hasReadAll }),
                DataObject(128, storyCurrency),
                DataObject(27, storyItem),
                DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 1, null, 63200, 63200 }),
                DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 1, null, 1, 1 }),
                DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 9, null, 42, 42 }),
                DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 5, null, 6, 6 })
            },
            new object?[]
            {
                new object?[] { 0, new object?[] { 1, 1 } },
                new object?[] { 0, new object?[] { 6, 6 } },
                new object?[] { 0, new object?[] { 63200, 63200 } }
            });
    }

    static async Task<object?[]> UpdateCharacterSideStoryStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        long characterMasterId,
        int? releasedOrder,
        int? readOrder)
    {
        await using (var command = new NpgsqlCommand(
            """
            update user_character_cards
            set extra_1 = greatest(extra_1, $3),
                extra_2 = greatest(extra_2, $4)
            where "userId" = $1
              and character_master_id = $2
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(characterMasterId);
            command.Parameters.AddWithValue(readOrder ?? 0);
            command.Parameters.AddWithValue(releasedOrder ?? 0);
            await command.ExecuteNonQueryAsync();
        }

        return await ReadCharacterCardAsync(connection, transaction, userId, characterMasterId);
    }

    static async Task UpsertEpisodeReadStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        long episodeMasterId, bool hasReadAll = false)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_episode_read_states ("userId", episode_master_id, is_new, has_read_all, read_at, updated_at)
            values ($1, $2, false, $3, now(), now())
            on conflict ("userId", episode_master_id)
            do update set is_new = false,
                          has_read_all = user_episode_read_states.has_read_all or excluded.has_read_all,
                          read_at = now(),
                          updated_at = now()
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(episodeMasterId);
        command.Parameters.AddWithValue(hasReadAll);
        await command.ExecuteNonQueryAsync();
    }

    static async Task<object?[]> ReadCharacterCardAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        long characterMasterId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, character_master_id, level, exp, awakening, character_base_id,
                   skill_level, extra_1, extra_2, is_new
            from user_character_cards
            where "userId" = $1
              and character_master_id = $2
            order by id
            limit 1
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(characterMasterId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new object?[]
            {
                UserScopedId(userId, 1001),
                characterMasterId,
                1,
                0,
                0,
                0,
                UserScopedId(userId, 2001),
                1,
                0,
                0,
                false,
                null,
                0,
                1,
                false
            };
        }

        return new object?[]
        {
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            0,
            reader.GetInt32(4),
            reader.GetInt64(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            false,
            null,
            0,
            1,
            reader.GetBoolean(9)
        };
    }

    static async Task<object?[]> ReadOrCreateItemPossessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        long itemMasterId)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_item_possessions (id, "userId", item_master_id, quantity)
            values ($1, $2, $3, 0)
            on conflict ("userId", item_master_id)
            do update set updated_at = now()
            returning id, item_master_id, quantity
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(UserScopedId(userId, itemMasterId));
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(itemMasterId);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new object?[] { reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2) };
    }

    static async Task<object?[]> AddItemPossessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        long itemMasterId,
        int quantity)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_item_possessions (id, "userId", item_master_id, quantity)
            values ($1, $2, $3, $4)
            on conflict ("userId", item_master_id)
            do update set quantity = user_item_possessions.quantity + excluded.quantity,
                          updated_at = now()
            returning id, item_master_id, quantity
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(UserScopedId(userId, itemMasterId));
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(itemMasterId);
        command.Parameters.AddWithValue(quantity);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new object?[] { reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2) };
    }

    static async Task AddFreeJewelsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        int amount)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_item_currencies ("userId", free_jewel, paid_jewel, coin)
            values ($1, $2, 0, 0)
            on conflict ("userId")
            do update set free_jewel = user_item_currencies.free_jewel + excluded.free_jewel
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(amount);
        await command.ExecuteNonQueryAsync();
    }

    static async Task<object?[]> ReadCurrencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select coin, free_jewel, paid_jewel
            from user_item_currencies
            where "userId" = $1
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new object?[] { userId, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2) };
    }

    static async Task<object?[]> UpsertCharacterResourceProgressAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        long characterBaseMasterId,
        int resourceType,
        long resourceMasterId,
        int level,
        int exp)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_character_resource_progress (
              id, "userId", character_base_master_id, resource_type,
              resource_master_id, level, exp, rank, progress
            )
            values ($1, $2, $3, $4, $5, $6, $7, 0, 0)
            on conflict ("userId", character_base_master_id, resource_type)
            do update set resource_master_id = excluded.resource_master_id,
                          level = greatest(user_character_resource_progress.level, excluded.level),
                          exp = greatest(user_character_resource_progress.exp, excluded.exp),
                          updated_at = now()
            returning id, character_base_master_id, resource_type, resource_master_id, level, exp, rank, progress
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(UserScopedId(userId, 96000 + characterBaseMasterId + resourceType));
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(characterBaseMasterId);
        command.Parameters.AddWithValue(resourceType);
        command.Parameters.AddWithValue(resourceMasterId);
        command.Parameters.AddWithValue(level);
        command.Parameters.AddWithValue(exp);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new object?[]
        {
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt32(2),
            reader.GetInt64(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7)
        };
    }
}
