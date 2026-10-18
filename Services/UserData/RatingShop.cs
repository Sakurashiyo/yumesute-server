using Npgsql;

sealed partial class UserDataService
{
    public Task<ShopPurchaseResponse> PurchaseShopItemAsync(HttpContext context, object? body)
    {
        if (body is not object?[] { Length: 1 } values || GetLong(values, 0) is not long productId)
            throw new BadHttpRequestException(PermanentMarketErrors.InvalidRequest, 400);
        return RatingShopRules.Products.TryGetValue(productId, out var product)
            ? PurchaseRatingRewardAsync(context, product)
            : PurchaseDailyFreePackAsync(context, body);
    }

    async Task<ShopPurchaseResponse> PurchaseRatingRewardAsync(HttpContext context, RatingShopRules.Product product)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var now = DateTimeOffset.UtcNow;
        if (now < product.Start || product.End is { } end && now >= end)
            throw new BadHttpRequestException(PermanentMarketErrors.Unavailable, 404);
        var purchases = await ReadShopPurchaseStatesAsync(connection, userId, transaction, now);
        var previous = purchases.SingleOrDefault(row => Convert.ToInt64(row[1]) == product.Id);
        var existing = (await ReadPermanentMarketDataAsync(connection, userId)).Cast<object?[]>()
            .Where(row => Convert.ToInt32(row[0]) == 44).Select(row => (object?[])row[1]!).SingleOrDefault();
        var colors = existing is null ? new[] { DefaultNameColorMasterId }
            : ((object?[])existing[1]!).Select(Convert.ToInt64).ToArray();
        var claimed = previous is not null && Convert.ToInt32(previous[2]) >= 1;
        if (!claimed)
        {
            if (await ReadPlayerRatingAsync(connection, userId) < product.RequiredRating)
                throw new BadHttpRequestException(PermanentMarketErrors.Unavailable, 409);
            colors = colors.Append(product.NameColorId).Distinct().Order().ToArray();
            var possession = new object?[] { existing?[0] ?? UserScopedId(userId, 44001), colors.Cast<object?>().ToArray() };
            await SavePermanentMarketDataAsync(connection, transaction, userId, 44, "colors", possession);
            await ExecuteAsync(connection, transaction,
                "update user_game_states set coin=coin+$2,updated_at=now() where \"userId\"=$1", userId, product.Coins);
            await ExecuteAsync(connection, transaction,
                "update user_item_currencies set coin=coin+$2 where \"userId\"=$1", userId, product.Coins);
            await ExecuteAsync(connection, transaction,
                """
                insert into user_shop_purchase_states
                  (id,"userId",shop_item_id,purchased_count,purchase_limit,payload)
                values($1,$2,$3,1,1,'{"totalPurchaseCount":1}'::jsonb)
                """, UserScopedId(userId, 108000 + product.Id), userId, product.Id);
            existing = possession;
        }
        if (existing is null || !colors.Contains(product.NameColorId))
            throw new InvalidDataException("已领取的 Rating 奖励缺少名称颜色持有记录");
        var purchase = (await ReadShopPurchaseStatesAsync(connection, userId, transaction, now))
            .Single(row => Convert.ToInt64(row[1]) == product.Id);
        var present = new object?[]
        {
            DataObject(44, existing), DataObject(108, purchase),
            DataObject(0, await ReadUserAsync(connection, userId)),
            DataObject(128, await ReadCurrencyAsync(connection, userId))
        };
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "Rating 奖励领取 operation=rating-shop-claim userId={UserId} productId={ProductId} nameColorId={NameColorId} replay={Replay} outcome=success",
            userId, product.Id, product.NameColorId, claimed);
        object?[] rewards = claimed ? [] :
        [
            new object?[] { 9, product.NameColorId, 1, null, null, null, false },
            new object?[] { 12, 0L, product.Coins, null, null, null, false }
        ];
        return new ShopPurchaseResponse(rewards, present);
    }
}
