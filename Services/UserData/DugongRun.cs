using Npgsql;

sealed partial class UserDataService
{
    const long DugongRunEventMasterId = 50000;
    const long DugongRunStatePublicId = 23435;
    const int DugongRunClearRewardItemMasterId = 140000;
    const int DugongRunNoMistakeRewardItemMasterId = 150000;

    public sealed record DugongRunClearResult(object?[] Rewards, object?[] PresentData);

    public async Task<DugongRunClearResult> ClearDugongRunCourseAsync(
        HttpContext context,
        long dugongRunCourseMasterId,
        int clearType)
    {
        if (clearType <= 0)
        {
            return new DugongRunClearResult(Array.Empty<object?>(), Array.Empty<object?>());
        }

        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null)
        {
            return BuildFallbackDugongRunClearResult(dugongRunCourseMasterId, clearType);
        }

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var state = await ReadDugongRunStateAsync(connection, transaction, userId.Value);
            var cleared = state.ClearedCourseIds.ToHashSet();
            var noMistake = state.NoMistakeCourseIds.ToHashSet();
            var courseId = (int)Math.Clamp(dugongRunCourseMasterId, 1, int.MaxValue);

            if (clearType >= 1)
            {
                cleared.Add(courseId);
            }

            if (clearType >= 2)
            {
                noMistake.Add(courseId);
            }

            var reward = DugongRunRewardFor(clearType);
            await UpsertDugongRunStateAsync(
                connection,
                transaction,
                userId.Value,
                cleared.Order().ToArray(),
                noMistake.Order().ToArray());
            await UpsertItemQuantityAsync(connection, transaction, userId.Value, reward.ItemMasterId, reward.Quantity);

            await transaction.CommitAsync();

            await using var readConnection = await database.OpenConnectionAsync();
            var itemRow = await ReadItemPossessionAsync(readConnection, userId.Value, reward.ItemMasterId)
                          ?? new object?[] { UserScopedId(userId.Value, reward.ItemMasterId), reward.ItemMasterId, reward.Quantity };

            return new DugongRunClearResult(
                new object?[] { BuildRewardRow(reward.ItemMasterId, reward.Quantity) },
                new object?[]
                {
                    DataObject(177, BuildDugongRunStateRow(userId.Value, cleared.Order().ToArray(), noMistake.Order().ToArray())),
                    DataObject(27, itemRow)
                });
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }
    }

    static DugongRunClearResult BuildFallbackDugongRunClearResult(long dugongRunCourseMasterId, int clearType)
    {
        var courseId = (int)Math.Clamp(dugongRunCourseMasterId, 1, int.MaxValue);
        var cleared = clearType >= 1 ? new[] { courseId } : Array.Empty<int>();
        var noMistake = clearType >= 2 ? new[] { courseId } : Array.Empty<int>();
        var reward = DugongRunRewardFor(clearType);
        return new DugongRunClearResult(
            new object?[] { BuildRewardRow(reward.ItemMasterId, reward.Quantity) },
            new object?[]
            {
                DataObject(177, new object?[] { 23435, cleared, noMistake, 1 }),
                DataObject(27, new object?[] { Random.Shared.Next(151000000, 151999999), reward.ItemMasterId, reward.Quantity })
            });
    }

    static (int ItemMasterId, int Quantity) DugongRunRewardFor(int clearType)
    {
        return clearType >= 2
            ? (DugongRunNoMistakeRewardItemMasterId, 15)
            : (DugongRunClearRewardItemMasterId, 150);
    }

    static object?[] BuildRewardRow(int itemMasterId, int quantity)
    {
        return new object?[] { 1, itemMasterId, quantity, null, null, null, false };
    }

    static object?[] BuildDugongRunStateRow(long userId, int[] clearedCourseIds, int[] noMistakeCourseIds)
    {
        return new object?[] { DugongRunStatePublicId, clearedCourseIds, noMistakeCourseIds, 1 };
    }

    static async Task<(int[] ClearedCourseIds, int[] NoMistakeCourseIds)> ReadDugongRunStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select cleared_course_ids, no_mistake_course_ids
            from user_dugong_run_states
            where "userId" = $1 and event_master_id = $2
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(DugongRunEventMasterId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (Array.Empty<int>(), Array.Empty<int>());

        return ((int[])reader[0], (int[])reader[1]);
    }

    static async Task UpsertDugongRunStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        int[] clearedCourseIds,
        int[] noMistakeCourseIds)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_dugong_run_states (
              id, "userId", event_master_id, cleared_course_ids, no_mistake_course_ids, status, updated_at
            )
            values ($1, $2, $3, $4, $5, 1, now())
            on conflict ("userId", event_master_id)
            do update set cleared_course_ids = excluded.cleared_course_ids,
                          no_mistake_course_ids = excluded.no_mistake_course_ids,
                          status = 1,
                          updated_at = now()
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(UserScopedId(userId, 17701));
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(DugongRunEventMasterId);
        command.Parameters.AddWithValue(clearedCourseIds);
        command.Parameters.AddWithValue(noMistakeCourseIds);
        await command.ExecuteNonQueryAsync();
    }

    static async Task<object?[]?> ReadItemPossessionAsync(NpgsqlConnection connection, long userId, long itemMasterId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, item_master_id, quantity
            from user_item_possessions
            where "userId" = $1 and item_master_id = $2
            """,
            connection);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(itemMasterId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new object?[] { reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2) };
    }
}
