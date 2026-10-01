using Npgsql;

sealed partial class UserDataService
{
    public sealed record ShopPurchaseResponse(object?[] Rewards, object?[] Present);

    public async Task<object?[]> GetShopPurchasePresentAsync(HttpContext context)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        return (await ReadShopPurchaseStatesAsync(connection, userId)).Select(row => (object?)DataObject(108, row)).ToArray();
    }

    async Task<List<object?[]>> ReadShopPurchaseStatesAsync(NpgsqlConnection connection, long userId,
        NpgsqlTransaction? transaction = null, DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var boundary = CurrentDailyResetBoundaryUtc(now).UtcDateTime;
        var next = boundary.AddDays(1);
        // 旧接口未保存购买记录；仅把当前游戏日已完成的免费包任务迁移成已领取状态，不追补奖励。
        await using (var migrate = new NpgsqlCommand("""
            insert into user_shop_purchase_states
              (id,"userId",shop_item_id,purchased_count,purchase_limit,reset_at,payload)
            select $1,$2,$3,1,$4,$5,'{"totalPurchaseCount":1}'::jsonb
            where exists(select 1 from user_mission_statuses where "userId"=$2 and mission_from=100400
              and is_cleared and completed_at >= $6 and completed_at < $5)
            on conflict ("userId",shop_item_id) do nothing
            """, connection, transaction))
        {
            migrate.Parameters.AddWithValue(UserScopedId(userId, 108000 + DailyFreePackRules.ProductId));
            migrate.Parameters.AddWithValue(userId);
            migrate.Parameters.AddWithValue(DailyFreePackRules.ProductId);
            migrate.Parameters.AddWithValue(DailyFreePackRules.PurchaseLimit);
            migrate.Parameters.AddWithValue(next);
            migrate.Parameters.AddWithValue(boundary);
            await migrate.ExecuteNonQueryAsync();
        }
        await using (var reset = new NpgsqlCommand("""
            update user_shop_purchase_states set purchased_count=0,reset_at=$3,updated_at=now()
            where "userId"=$1 and shop_item_id=$2 and reset_at <= $4
            """, connection, transaction))
        {
            reset.Parameters.AddWithValue(userId);
            reset.Parameters.AddWithValue(DailyFreePackRules.ProductId);
            reset.Parameters.AddWithValue(next);
            reset.Parameters.AddWithValue(now.UtcDateTime);
            await reset.ExecuteNonQueryAsync();
        }
        await using var command = new NpgsqlCommand("""
            select id,shop_item_id,purchased_count,
              coalesce((payload->>'totalPurchaseCount')::integer,purchased_count),reset_at
            from user_shop_purchase_states where "userId"=$1 order by shop_item_id
            """, connection, transaction);
        command.Parameters.AddWithValue(userId);
        var rows = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new object?[] { reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetDateTime(4) });
        return rows;
    }

    public async Task<ShopPurchaseResponse> PurchaseDailyFreePackAsync(HttpContext context, object? body)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        if (body is not object?[] { Length: 1 } values || GetLong(values, 0) is not long productId)
            throw new BadHttpRequestException(PermanentMarketErrors.InvalidRequest, 400);
        if (productId != DailyFreePackRules.ProductId)
            throw new BadHttpRequestException(PermanentMarketErrors.Unavailable, 404);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // 玩家状态行锁与其他奖励/货币写入共用，串行化同账号的重复点击和并发领取。
        await LockLessonUserAsync(connection, transaction, userId);
        var now = DateTimeOffset.UtcNow;
        var previous = (await ReadShopPurchaseStatesAsync(connection, userId, transaction, now))
            .SingleOrDefault(row => Convert.ToInt64(row[1]) == productId);
        if (previous is not null && Convert.ToInt32(previous[2]) >= DailyFreePackRules.PurchaseLimit)
            throw new BadHttpRequestException(PermanentMarketErrors.LimitReached, 409);
        var next = CurrentDailyResetBoundaryUtc(now).AddDays(1).UtcDateTime;
        await using (var purchase = new NpgsqlCommand("""
            insert into user_shop_purchase_states
              (id,"userId",shop_item_id,purchased_count,purchase_limit,reset_at,payload)
            values($1,$2,$3,1,$4,$5,'{"totalPurchaseCount":1}'::jsonb)
            on conflict ("userId",shop_item_id) do update set
              purchased_count=user_shop_purchase_states.purchased_count+1,
              purchase_limit=excluded.purchase_limit,reset_at=excluded.reset_at,
              payload=jsonb_set(user_shop_purchase_states.payload,'{totalPurchaseCount}',
                to_jsonb(coalesce((user_shop_purchase_states.payload->>'totalPurchaseCount')::integer,
                  user_shop_purchase_states.purchased_count)+1)),updated_at=now()
            """, connection, transaction))
        {
            purchase.Parameters.AddWithValue(UserScopedId(userId, 108000 + productId));
            purchase.Parameters.AddWithValue(userId);
            purchase.Parameters.AddWithValue(productId);
            purchase.Parameters.AddWithValue(DailyFreePackRules.PurchaseLimit);
            purchase.Parameters.AddWithValue(next);
            await purchase.ExecuteNonQueryAsync();
        }
        var coin = DailyFreePackRules.Rewards.Where(reward => reward.Type == 12).Sum(reward => reward.Quantity);
        var stamina = DailyFreePackRules.Rewards.Where(reward => reward.Type == 15).Sum(reward => reward.Quantity);
        await ExecuteAsync(connection, transaction,
            "update user_game_states set coin=coin+$2,stamina=stamina+$3,updated_at=now() where \"userId\"=$1", userId, coin, stamina);
        await ExecuteAsync(connection, transaction,
            "update user_item_currencies set coin=coin+$2 where \"userId\"=$1", userId, coin);
        await CompleteShopFreePackMissionsAsync(connection, transaction, userId);
        var present = new List<object?>
        {
            DataObject(0, await ReadUserAsync(connection, userId)),
            DataObject(128, await ReadCurrencyAsync(connection, userId))
        };
        present.AddRange((await ReadShopPurchaseStatesAsync(connection, userId, transaction, now)).Select(row => DataObject(108, row)));
        present.AddRange((await ReadMissionsAsync(connection, userId)).Where(row => Convert.ToInt64(row[5]) is 100300 or 100400)
            .Select(row => DataObject(48, row)));
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "每日免费包领取完成 operation={Operation} userId={UserId} productId={ProductId} resetAt={ResetAt}",
            "shop-purchase", userId, productId, next);
        var rewards = DailyFreePackRules.Rewards.Select(reward => (object?)new object?[]
            { reward.Type, 0L, reward.Quantity, null, null, null, false }).ToArray();
        return new ShopPurchaseResponse(rewards, present.ToArray());
    }
}
