using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    // 保留旧存档的内部编号，客户端 Poster 的 IDataObject union 编号为 11。
    const int LegacyPosterStorageKey = 28;
    const int PosterDataObjectUnionKey = 11;

    public sealed record PermanentMarketExchangeResult(object?[] Rewards, object?[] Present);

    public async Task<PermanentMarketExchangeResult> ExchangePermanentMarketThingAsync(HttpContext context, long productId, int quantity)
    {
        if(quantity <= 0) throw new BadHttpRequestException(PermanentMarketErrors.InvalidRequest,400);
        var now = DateTimeOffset.UtcNow;
        if(!PermanentMarketRules.Products.TryGetValue(productId,out var product)
            || product.Start > now || product.End <= now)
            throw new BadHttpRequestException(PermanentMarketErrors.Unavailable,404);
        var cost = (long)product.Cost * quantity;
        var gain = (long)product.Quantity * quantity;
        if(cost < 0 || cost > int.MaxValue || gain <= 0 || gain > int.MaxValue)
            throw new BadHttpRequestException(PermanentMarketErrors.InvalidRequest,400);
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException("ACCOUNT_UNAUTHORIZED",401);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection,transaction,userId);
        var stored = await ReadPermanentMarketDataAsync(connection,userId);
        var previous = stored.Cast<object?[]>().Where(row => Convert.ToInt32(row[0])==149)
            .Select(row => (object?[])row[1]!).SingleOrDefault(row => Convert.ToInt64(row[0])==productId);
        var bought = previous is null ? 0 : Convert.ToInt32(previous[1]);
        if((long)bought+quantity > int.MaxValue || product.Limit is int limit && (long)bought+quantity > limit)
            throw new BadHttpRequestException(PermanentMarketErrors.LimitReached,409);
        var currency = await ReadCurrencyAsync(connection,userId);
        var coin = Convert.ToInt32(currency[1]);
        var free = Convert.ToInt32(currency[2]);
        var paid = Convert.ToInt32(currency[3]);
        if(product.CostType == 12)
        {
            if(coin < cost) throw new BadHttpRequestException(PermanentMarketErrors.Insufficient,409);
            coin -= (int)cost;
        }
        else if(product.CostType == 13)
        {
            if((long)free+paid < cost) throw new BadHttpRequestException(PermanentMarketErrors.Insufficient,409);
            var freeCost = Math.Min(free,(int)cost);
            free -= freeCost; paid -= (int)cost-freeCost;
        }
        else if(product.CostType == 1)
        {
            var changed = await ExecuteMarketItemCostAsync(connection,transaction,userId,product.CostId,(int)cost);
            if(changed != 1) throw new BadHttpRequestException(PermanentMarketErrors.Insufficient,409);
        }
        else throw new InvalidOperationException($"常设商品消耗类型尚未实现：{product.CostType}");
        var present = new List<object?>();
        if(product.Type == 1) await UpsertItemQuantityAsync(connection,transaction,userId,product.ThingId,(int)gain);
        else if(product.Type == 12)
        {
            if((long)coin+gain > int.MaxValue) throw new BadHttpRequestException(PermanentMarketErrors.InvalidRequest,400);
            coin += (int)gain;
        }
        else if(product.Type is 8 or 16 or 19)
        {
            if(gain != 1) throw new BadHttpRequestException(PermanentMarketErrors.InvalidRequest,400);
            var key = product.Type switch { 8=>45,16=>129,_=>182 };
            object?[] value;
            if(key == 182)
            {
                var existing = stored.Cast<object?[]>().Where(row => Convert.ToInt32(row[0])==182).Select(row=>(object?[])row[1]!).SingleOrDefault();
                var frames = existing is null ? new[] {DefaultIconFrameMasterId} : ((object?[])existing[1]!).Select(Convert.ToInt64).ToArray();
                if(frames.Contains(product.ThingId)) throw new BadHttpRequestException(PermanentMarketErrors.LimitReached,409);
                value = new object?[] {UserScopedId(userId,18201),frames.Append(product.ThingId).Cast<object?>().ToArray()};
            }
            else
            {
                if(stored.Cast<object?[]>().Any(row => Convert.ToInt32(row[0])==key && Convert.ToInt64(((object?[])row[1]!)[1])==product.ThingId))
                    throw new BadHttpRequestException(PermanentMarketErrors.LimitReached,409);
                value = new object?[] {UserScopedId(userId,key*10000+productId),product.ThingId};
            }
            await SavePermanentMarketDataAsync(connection,transaction,userId,key,key==182?"frames":product.ThingId.ToString(),value);
            present.Add(DataObject(key,value));
        }
        else throw new InvalidOperationException($"常设商品奖励类型尚未实现：{product.Type}");
        await ExecuteAsync(connection,transaction,"update user_item_currencies set coin=$2,free_jewel=$3,paid_jewel=$4 where \"userId\"=$1",userId,coin,free,paid);
        await ExecuteAsync(connection,transaction,"update user_game_states set coin=$2,free_jewel=$3,paid_jewel=$4,updated_at=now() where \"userId\"=$1",userId,coin,free,paid);
        var purchase = new object?[] {productId,bought+quantity};
        await SavePermanentMarketDataAsync(connection,transaction,userId,149,productId.ToString(),purchase);
        present.Add(DataObject(149,purchase));
        present.Add(DataObject(0,await ReadUserAsync(connection,userId)));
        present.Add(DataObject(128,await ReadCurrencyAsync(connection,userId)));
        foreach(var item in await ReadItemPossessionsAsync(connection,userId))
            if(Convert.ToInt64(item[1])==product.CostId && product.CostType==1 || Convert.ToInt64(item[1])==product.ThingId && product.Type==1)
                present.Add(DataObject(27,item));
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "常设商店购买成功 operation={Operation} userId={UserId} productId={ProductId} quantity={Quantity}","permanent-market-exchange",userId,productId,quantity);
        return new(new object?[] {new object?[] {product.Type,product.ThingId,(int)gain,null,null,null,false}},present.ToArray());
    }

    static async Task<int> ExecuteMarketItemCostAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,long userId,long itemId,int cost)
    {
        await using var command = new NpgsqlCommand("update user_item_possessions set quantity=quantity-$3,updated_at=now() where \"userId\"=$1 and item_master_id=$2 and quantity>=$3",connection,transaction);
        command.Parameters.AddWithValue(userId);command.Parameters.AddWithValue(itemId);command.Parameters.AddWithValue(cost);
        return await command.ExecuteNonQueryAsync();
    }

    static async Task SavePermanentMarketDataAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,long userId,int key,string objectKey,object?[] value)
    {
        await ExecuteAsync(connection,transaction,"""
            insert into user_raw_union_states ("userId",union_key,object_key,payload) values ($1,$2,$3,$4::jsonb)
            on conflict ("userId",union_key,object_key) do update set payload=excluded.payload,updated_at=now()
            """,userId,key,objectKey,JsonSerializer.Serialize(value));
    }

    static async Task<object?[]> ReadPermanentMarketDataAsync(NpgsqlConnection connection,long userId)
    {
        await using var command = new NpgsqlCommand("select union_key,payload::text from user_raw_union_states where \"userId\"=$1 and union_key in (28,45,129,149,182) order by union_key,object_key",connection);
        command.Parameters.AddWithValue(userId);
        var data = new List<object?>();
        static object Read(JsonElement field) => field.ValueKind switch
        {
            JsonValueKind.Array => field.EnumerateArray().Select(Read).ToArray(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => field.GetInt64(),
            _ => throw new InvalidOperationException("物品协议存档包含不支持的字段类型")
        };
        await using var reader = await command.ExecuteReaderAsync();
        while(await reader.ReadAsync())
        {
            using var json = JsonDocument.Parse(reader.GetString(1));
            var storageKey = reader.GetInt32(0);
            var protocolKey = storageKey == LegacyPosterStorageKey ? PosterDataObjectUnionKey : storageKey;
            data.Add(DataObject(protocolKey,(object?[])Read(json.RootElement)));
        }
        return data.ToArray();
    }
}
