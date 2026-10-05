using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> UseStaminaRecoveryItemsAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return Array.Empty<object?>();

        await EnsureDefaultUserDataAsync(userId.Value);
        var requestedItems = ParseStaminaRecoveryItemUsages(payload);
        if (requestedItems.Count == 0)
        {
            await using var readOnlyConnection = await database.OpenConnectionAsync();
            return new object?[] { DataObject(0, await ReadUserAsync(readOnlyConnection, userId.Value)) };
        }

        var presentData = new List<object?[]>();
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var totalRecovery = 0;
            foreach (var (itemMasterId, requestedQuantity) in requestedItems)
            {
                var quantity = Math.Max(requestedQuantity, 0);
                if (quantity <= 0) continue;

                totalRecovery += CalculateStaminaItemRecovery(itemMasterId) * quantity;
                var itemRow = await ConsumeItemPossessionAsync(connection, transaction, userId.Value, itemMasterId, quantity);
                presentData.Add(DataObject(27, itemRow));
            }

            if (totalRecovery > 0)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    update user_game_states
                    set stamina = stamina + $2,
                        stamina_recovered_at = now(),
                        updated_at = now()
                    where "userId" = $1
                    """,
                    userId.Value,
                    totalRecovery);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }

        presentData.Add(DataObject(0, await ReadUserAsync(connection, userId.Value)));
        return presentData.ToArray();
    }

    public async Task<object?[]> RecoverStaminaByJewelAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return Array.Empty<object?>();

        await EnsureDefaultUserDataAsync(userId.Value);
        var times = Math.Max(ReadIntQuery(context, "times") ?? 1, 1);

        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var (rank, maxStamina) = await ReadRankAndMaxStaminaAsync(connection, transaction, userId.Value);
            var recovery = CalculateJewelStaminaRecovery(rank, maxStamina, times);
            var jewelCost = CalculateJewelStaminaRecoveryCost(times);

            await ExecuteAsync(
                connection,
                transaction,
                """
                update user_game_states
                set stamina = stamina + $2,
                    stamina_jewel_recovery_count = greatest(stamina_jewel_recovery_count, $3),
                    free_jewel = greatest(free_jewel - $4, 0),
                    paid_jewel = case
                      when free_jewel >= $4 then paid_jewel
                      else greatest(paid_jewel - ($4 - free_jewel), 0)
                    end,
                    stamina_recovered_at = now(),
                    updated_at = now()
                where "userId" = $1
                """,
                userId.Value,
                recovery,
                times,
                jewelCost);

            await ExecuteAsync(
                connection,
                transaction,
                """
                update user_item_currencies
                set free_jewel = greatest(free_jewel - $2, 0),
                    paid_jewel = case
                      when free_jewel >= $2 then paid_jewel
                      else greatest(paid_jewel - ($2 - free_jewel), 0)
                    end
                where "userId" = $1
                """,
                userId.Value,
                jewelCost);

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }

        return new object?[]
        {
            DataObject(128, await ReadCurrencyAsync(connection, userId.Value)),
            DataObject(0, await ReadUserAsync(connection, userId.Value))
        };
    }

    static List<(long ItemMasterId, int Quantity)> ParseStaminaRecoveryItemUsages(object? payload)
    {
        var result = new List<(long, int)>();
        if (payload is not object?[] root) return result;

        var itemRows = root.Length == 1 && root[0] is object?[] nestedRows
            ? nestedRows
            : root;

        foreach (var item in itemRows)
        {
            if (item is not object?[] row) continue;
            var itemMasterId = GetLong(row, 0);
            var quantity = GetInt(row, 1);
            if (itemMasterId is null || quantity is null) continue;
            result.Add((itemMasterId.Value, quantity.Value));
        }

        return result;
    }

    static int CalculateStaminaItemRecovery(long itemMasterId)
    {
        return itemMasterId switch
        {
            110001 => 30,
            110002 => 100,
            110003 => 200,
            >= 111000 and < 112000 => 100,
            >= 112000 and < 120000 => 100,
            _ => 0
        };
    }

    static int CalculateJewelStaminaRecovery(int rank, int maxStamina, int times)
    {
        // 官方包中 rank=18、times=4 时恢复 920；这里先用「随等级增长的等效最大体力」做近似，
        // 后续如果 MasterData 里定位到精确的体力上限公式，再替换为表驱动计算。
        var effectiveMaxStamina = Math.Max(maxStamina, 50 + (rank * 10));
        return effectiveMaxStamina * times;
    }

    static int CalculateJewelStaminaRecoveryCost(int times)
    {
        // 官方 times=4 的抓包扣了 230 钻；先按第 n 次恢复费用 50 + 60 * (n - 1) 近似。
        return Math.Max(0, 50 + ((times - 1) * 60));
    }

    static async Task<(int Rank, int MaxStamina)> ReadRankAndMaxStaminaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select rank, max_stamina
            from user_game_states
            where "userId" = $1
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (1, 50);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    static async Task<object?[]> ConsumeItemPossessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        long itemMasterId,
        int quantity)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_item_possessions (id, "userId", item_master_id, quantity)
            values ($1, $2, $3, 0)
            on conflict ("userId", item_master_id)
            do update set quantity = greatest(user_item_possessions.quantity - $4, 0),
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
}
