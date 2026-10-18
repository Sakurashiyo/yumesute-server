using Npgsql;

sealed partial class UserDataService
{
    async Task<ShopPurchaseResponse> PurchaseJewelPackAsync(HttpContext context, JewelShopRules.Product product)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var now = DateTimeOffset.UtcNow;
        if (now < product.Start || product.End is { } end && now >= end)
            throw new BadHttpRequestException(PermanentMarketErrors.Unavailable, 404);
        if (product.UnlockType == 8 && await ReadPlayerRatingAsync(connection, userId) < product.UnlockValue
            || product.UnlockType == 16 && (await ReadRankStateAsync(connection, transaction, userId)).Rank < product.UnlockValue)
            throw new BadHttpRequestException(PermanentMarketErrors.Unavailable, 409);
        var previous = (await ReadShopPurchaseStatesAsync(connection, userId, transaction, now))
            .SingleOrDefault(row => Convert.ToInt64(row[1]) == product.Id);
        if (product.Limit is { } limit && previous is not null && Convert.ToInt32(previous[2]) >= limit)
        {
            // 单次限购允许安全重试；多次限购没有请求幂等键，超限必须明确拒绝。
            if (limit != 1) throw new BadHttpRequestException(PermanentMarketErrors.LimitReached, 409);
            var synchronized = await BuildJewelPackPresentAsync(connection, userId, previous);
            await transaction.CommitAsync();
            return new ShopPurchaseResponse([], synchronized);
        }
        var currency = await ReadCurrencyAsync(connection, userId);
        if (Convert.ToInt64(currency[3]) < product.Price)
            throw new BadHttpRequestException(PermanentMarketErrors.Insufficient, 409);
        var coinGain = product.Rewards.Where(reward => reward.Type == 12).Sum(reward => reward.Quantity);
        await ExecuteAsync(connection, transaction,
            "update user_game_states set paid_jewel=paid_jewel-$2,coin=coin+$3,updated_at=now() where \"userId\"=$1",
            userId, product.Price, coinGain);
        await ExecuteAsync(connection, transaction,
            "update user_item_currencies set paid_jewel=paid_jewel-$2,coin=coin+$3 where \"userId\"=$1",
            userId, product.Price, coinGain);
        foreach (var reward in product.Rewards)
            await GrantJewelPackRewardAsync(connection, transaction, userId, reward);
        await using (var command = new NpgsqlCommand("""
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
            command.Parameters.AddWithValue(UserScopedId(userId, 108000 + product.Id));
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(product.Id);
            command.Parameters.Add(new NpgsqlParameter<int?> { TypedValue = product.Limit });
            command.Parameters.Add(new NpgsqlParameter<DateTime?> { TypedValue =
                product.ResetType == 3 ? JewelShopRules.NextMonthlyReset(now).UtcDateTime : null });
            await command.ExecuteNonQueryAsync();
        }
        var purchase = (await ReadShopPurchaseStatesAsync(connection, userId, transaction, now))
            .Single(row => Convert.ToInt64(row[1]) == product.Id);
        var present = await BuildJewelPackPresentAsync(connection, userId, purchase);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "宝石商品购买 operation=jewel-shop-purchase userId={UserId} productId={ProductId} paidCost={PaidCost} outcome=success",
            userId, product.Id, product.Price);
        return new ShopPurchaseResponse(product.Rewards.Select(reward => (object?)new object?[]
            { reward.Type, reward.MasterId, reward.Quantity, null, null, null, false }).ToArray(), present);
    }

    static async Task GrantJewelPackRewardAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long userId, JewelShopRules.Reward reward)
    {
        switch (reward.Type)
        {
            case 1:
                await UpsertItemQuantityAsync(connection, transaction, userId, reward.MasterId, reward.Quantity);
                break;
            case 12:
                break;
            case 16:
                // 同一装饰可出现在多个礼包；沿用已有持有记录的 Id，避免客户端重复显示。
                await using (var command = new NpgsqlCommand("""
                    insert into user_raw_union_states ("userId",union_key,object_key,payload)
                    values($1,129,$2,$3::jsonb)
                    on conflict ("userId",union_key,object_key) do nothing
                    """, connection, transaction))
                {
                    command.Parameters.AddWithValue(userId);
                    command.Parameters.AddWithValue(reward.MasterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue(System.Text.Json.JsonSerializer.Serialize(new object?[]
                        { UserScopedId(userId, 1290000 + reward.MasterId), reward.MasterId }));
                    await command.ExecuteNonQueryAsync();
                }
                break;
            case 17:
                var stored = (await ReadPermanentMarketDataAsync(connection, userId)).Cast<object?[]>()
                    .Where(row => Convert.ToInt32(row[0]) == 181).Select(row => (object?[])row[1]!).SingleOrDefault();
                var colors = stored is null ? new[] { DefaultNameBaseColorMasterId }
                    : ((object?[])stored[1]!).Select(Convert.ToInt64).ToArray();
                await SavePermanentMarketDataAsync(connection, transaction, userId, 181, "base-colors",
                    [stored?[0] ?? UserScopedId(userId, 18101),
                        colors.Append(reward.MasterId).Distinct().Order().Cast<object?>().ToArray()]);
                break;
            default:
                throw new InvalidOperationException($"宝石商品奖励类型尚未实现：{reward.Type}");
        }
    }

    async Task<object?[]> BuildJewelPackPresentAsync(NpgsqlConnection connection, long userId, object?[] purchase)
    {
        var present = new List<object?>
        {
            DataObject(108, purchase), DataObject(0, await ReadUserAsync(connection, userId)),
            DataObject(128, await ReadCurrencyAsync(connection, userId))
        };
        present.AddRange((await ReadItemPossessionsAsync(connection, userId)).Select(item => DataObject(27, item)));
        present.AddRange((await ReadPermanentMarketDataAsync(connection, userId)).Cast<object?[]>()
            .Where(row => Convert.ToInt32(row[0]) is 129 or 181));
        return present.ToArray();
    }
}
