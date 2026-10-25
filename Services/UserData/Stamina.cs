using Npgsql;

sealed partial class UserDataService
{
    const int StaminaRecoverySeconds = 80;

    static async Task RefreshStaminaAsync(NpgsqlConnection connection, long userId, NpgsqlTransaction? transaction = null)
    {
        // 单条语句内锁定余额并推进完整恢复周期，并发读取不能重复补发。
        await using var command = new NpgsqlCommand(
            """
            with current_state as materialized (
              select "userId", stamina, max_stamina, stamina_recovery_checkpoint_at, now() as current_time,
                least(max_stamina - stamina,
                  greatest(0, floor(extract(epoch from (now() - stamina_recovery_checkpoint_at)) / $2)))::integer as recovered
              from user_game_states where "userId"=$1 and stamina < max_stamina for update
            )
            update user_game_states as target
            set stamina = current_state.stamina + current_state.recovered,
                stamina_recovery_checkpoint_at = case
                  when current_state.stamina + current_state.recovered >= current_state.max_stamina then current_state.current_time
                  else current_state.stamina_recovery_checkpoint_at + current_state.recovered * $2 * interval '1 second'
                end,
                updated_at = now()
            from current_state
            where target."userId" = current_state."userId" and current_state.recovered > 0
            """, connection, transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(StaminaRecoverySeconds);
        await command.ExecuteNonQueryAsync();
    }

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
            // 与其它货币路径统一加账号锁，避免并发重复消耗与先扣道具后加体力交错。
            await LockLessonUserAsync(connection, transaction, userId.Value);
            var totalRecovery = 0;
            foreach (var (itemMasterId, requestedQuantity) in requestedItems)
            {
                var quantity = Math.Max(requestedQuantity, 0);
                if (quantity <= 0) continue;

                var recoverPerItem = CalculateStaminaItemRecovery(itemMasterId);
                if (recoverPerItem <= 0) continue;
                // 未持有足够道具时整个请求失败，不能先钳到 0 再照常发放体力。
                var itemRow = await ConsumeItemPossessionAsync(connection, transaction, userId.Value, itemMasterId, quantity);
                totalRecovery = checked(totalRecovery + checked(recoverPerItem * quantity));
                presentData.Add(DataObject(27, itemRow));
            }

            if (totalRecovery > 0)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    update user_game_states
                    set stamina = greatest(stamina, max_stamina) + least($2, greatest(max_stamina - stamina, 0)),
                        stamina_recovered_at = now(),
                        stamina_recovery_checkpoint_at = now(),
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
        var times = ReadIntQuery(context, "times") ?? 1;
        // 恢复次数直接决定费用与回复量，必须限定在官方区间内，避免负数/溢出把费用算成 0。
        if (times is < 1 or > 10)
            throw new BadHttpRequestException(StaminaErrors.InvalidRecoveryCount, StatusCodes.Status400BadRequest);

        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // 与其它货币路径共用账号锁，避免并发双发各自加一次体力。
            await LockLessonUserAsync(connection, transaction, userId.Value);
            var (rank, maxStamina) = await ReadRankAndMaxStaminaAsync(connection, transaction, userId.Value);
            var recovery = CalculateJewelStaminaRecovery(rank, maxStamina, times);
            var jewelCost = CalculateJewelStaminaRecoveryCost(times);
            var currency = await ReadCurrencyAsync(connection, transaction, userId.Value);
            var free = Convert.ToInt32(currency[2]);
            var paid = Convert.ToInt32(currency[3]);
            if ((long)free + paid < jewelCost)
                throw new BadHttpRequestException(StaminaErrors.JewelInsufficient, StatusCodes.Status409Conflict);
            var freeCost = Math.Min(free, jewelCost);
            var nextFree = free - freeCost;
            var nextPaid = checked(paid - (jewelCost - freeCost));

            await ExecuteAsync(
                connection,
                transaction,
                """
                update user_game_states
                set stamina = greatest(stamina, max_stamina) + least($2, greatest(max_stamina - stamina, 0)),
                    stamina_jewel_recovery_count = greatest(stamina_jewel_recovery_count, $3),
                    free_jewel = $4,
                    paid_jewel = $5,
                    stamina_recovered_at = now(),
                    stamina_recovery_checkpoint_at = now(),
                    updated_at = now()
                where "userId" = $1
                """,
                userId.Value,
                recovery,
                times,
                nextFree,
                nextPaid);

            await ExecuteAsync(
                connection,
                transaction,
                """
                update user_item_currencies
                set free_jewel = $2,
                    paid_jewel = $3
                where "userId" = $1
                """,
                userId.Value,
                nextFree,
                nextPaid);

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
        if (times is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(times));
        return checked(50 + ((times - 1) * 60));
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
        // 带持有量守卫的增量扣减；影响行数为 0 说明道具不足，必须让整个请求失败。
        await using var command = new NpgsqlCommand(
            """
            update user_item_possessions
            set quantity = quantity - $3,
                updated_at = now()
            where "userId" = $1 and item_master_id = $2 and quantity >= $3
            returning id, item_master_id, quantity
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(itemMasterId);
        command.Parameters.AddWithValue(quantity);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new BadHttpRequestException(StaminaErrors.ItemInsufficient, StatusCodes.Status409Conflict);
        return new object?[] { reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2) };
    }
}
