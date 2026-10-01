using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    public sealed record GachaPurchase(object?[] Result,object?[] Present);
    public async Task<GachaPurchase> RollGachaAsync(HttpContext context,long detailId)
    {
        if(!GachaRules.Details.TryGetValue(detailId,out var detail))throw new BadHttpRequestException(GachaErrors.Unavailable,404);
        var userId=await RequireAuthenticatedUserAsync(context);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection=await database.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection,transaction,userId);
        var currency=await ReadCurrencyAsync(connection,userId);
        var free=Convert.ToInt32(currency[2]);var paid=Convert.ToInt32(currency[3]);
        if((long)free+paid<detail.Cost)throw new BadHttpRequestException(GachaErrors.Insufficient,409);
        var freeCost=Math.Min(free,detail.Cost);free-=freeCost;paid-=detail.Cost-freeCost;
        await ExecuteAsync(connection,transaction,"update user_item_currencies set free_jewel=$2,paid_jewel=$3 where \"userId\"=$1",userId,free,paid);
        await ExecuteAsync(connection,transaction,"update user_game_states set free_jewel=$2,paid_jewel=$3,updated_at=now() where \"userId\"=$1",userId,free,paid);
        var rewards=new List<object?>();var changed=new List<object?>();
        for(var i=0;i<detail.Count;i++)
        {
            var entry=GachaRules.Draw(detail.Pool,detail.Count==10 && i==9);
            var received=await GrantGachaEntryAsync(connection,transaction,userId,entry,changed);
            rewards.Add(new object?[] {new object?[] {received},Array.Empty<object?>(),null});
            await ExecuteAsync(connection,transaction,"""
                insert into user_gacha_histories("userId",gacha_detail_master_id,thing_type,thing_master_id,quantity,payload)
                values($1,$2,$3,$4,1,$5::jsonb)
                """,userId,detailId,entry.Type,entry.MasterId,JsonSerializer.Serialize(received));
        }
        var point=await AddItemPossessionAsync(connection,transaction,userId,detail.Pool.PointId,detail.Count);
        changed.Add(DataObject(27,point));
        var present=await GachaInventoryPresentAsync(connection,userId,changed);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "抽卡完成 operation={Operation} userId={UserId} poolId={PoolId} count={Count} jewelCost={Cost}","gacha-roll",userId,detail.Pool.Id,detail.Count,detail.Cost);
        return new([detail.Pool.Id,rewards.ToArray(),new object?[] {GachaRules.Received(1,detail.Pool.PointId,detail.Count)},Array.Empty<object?>(),Array.Empty<object?>()],present);
    }

    public async Task<GachaPurchase> ExchangeGachaAsync(HttpContext context,(long Id,int Quantity)[] requests)
    {
        if(requests.Length is <1 or >100 || requests.Any(r=>r.Quantity is <1 or >100))throw new BadHttpRequestException(GachaErrors.Invalid,400);
        var products=requests.Select(r=> GachaRules.Exchanges.TryGetValue(r.Id,out var product)?(Product:product,r.Quantity):throw new BadHttpRequestException(GachaErrors.Unavailable,404)).ToArray();
        var userId=await RequireAuthenticatedUserAsync(context);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection=await database.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection,transaction,userId);
        var changed=new List<object?>();var rewards=new List<object?>();
        foreach(var product in products)
        {
            if(await ExecuteMarketItemCostAsync(connection,transaction,userId,product.Product.Pool.PointId,product.Quantity*GachaRules.ExchangeCost)!=1)
                throw new BadHttpRequestException(GachaErrors.Insufficient,409);
            for(var i=0;i<product.Quantity;i++)rewards.Add(await GrantGachaEntryAsync(connection,transaction,userId,product.Product.Entry,changed));
        }
        var pointIds=products.Select(p=>p.Product.Pool.PointId).ToHashSet();
        foreach(var item in await ReadItemPossessionsAsync(connection,userId))
            if(pointIds.Contains(Convert.ToInt64(item[1])))changed.Add(DataObject(27,item));
        var present=await GachaInventoryPresentAsync(connection,userId,changed);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "抽卡点兑换完成 operation={Operation} userId={UserId} count={Count}","gacha-exchange",userId,requests.Sum(r=>r.Quantity));
        return new(rewards.ToArray(),present);
    }

    async Task<object?[]> GrantGachaEntryAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,long userId,GachaRules.Entry entry,List<object?> changed)
    {
        if(entry.Type==2)
        {
            if(await CardInventoryWriter.GrantAsync(connection,transaction,userId,CharacterCatalog.Value,CharacterCatalog.Value.Cards[entry.MasterId]))
            {
                foreach(var costume in entry.Costumes)
                    await ExecuteAsync(connection,transaction,"insert into user_character_costumes(id,\"userId\",costume_master_id) values($1,$2,$3) on conflict (\"userId\",costume_master_id) do nothing",CardInventoryWriter.NewId(),userId,costume);
                return GachaRules.Received(2,entry.MasterId,1);
            }
        }
        else
        {
            var stored=await ReadPermanentMarketDataAsync(connection,userId);
            var poster=stored.Cast<object?[]>().Where(r=>Convert.ToInt32(r[0])==PosterDataObjectUnionKey).Select(r=>(object?[])r[1]!).SingleOrDefault(r=>Convert.ToInt64(r[1])==entry.MasterId);
            if(poster is null || Convert.ToInt32(poster[3])<entry.MaxPhase)
            {
                poster=poster is null?new object?[] {CardInventoryWriter.NewId(),entry.MasterId,1,0,0,0,false,0}:(object?[])poster.Clone();
                if(stored.Cast<object?[]>().Any(r=>Convert.ToInt32(r[0])==PosterDataObjectUnionKey && Convert.ToInt64(((object?[])r[1]!)[1])==entry.MasterId))poster[3]=Convert.ToInt32(poster[3])+1;
                await SavePermanentMarketDataAsync(connection,transaction,userId,LegacyPosterStorageKey,entry.MasterId.ToString(),poster);
                changed.Add(DataObject(PosterDataObjectUnionKey,poster));
                return GachaRules.Received(3,entry.MasterId,1,phase:Convert.ToInt32(poster[3]));
            }
            if(Convert.ToInt32(poster[3])>entry.MaxPhase)throw new InvalidOperationException("海报存档超过突破上限");
        }
        var quantity=GachaRules.DuplicateQuantity(entry);
        var item=await AddItemPossessionAsync(connection,transaction,userId,GachaRules.DuplicateItemId,quantity);
        if(Convert.ToInt32(item[2])>CharacterCatalog.Value.Items[GachaRules.DuplicateItemId].MaxQuantity)
            throw new BadHttpRequestException(GachaErrors.Invalid,409);
        changed.Add(DataObject(27,item));
        return GachaRules.Received(1,GachaRules.DuplicateItemId,quantity,entry.Type,entry.MasterId);
    }

    async Task<object?[]> GachaInventoryPresentAsync(NpgsqlConnection connection,long userId,List<object?> changed)
    {
        changed.Add(DataObject(0,await ReadUserAsync(connection,userId)));
        changed.Add(DataObject(128,await ReadCurrencyAsync(connection,userId)));
        changed.AddRange((await ReadCharactersAsync(connection,userId)).Select(row=>DataObject(4,row)));
        changed.AddRange((await ReadCharacterBasesAsync(connection,userId)).Select(row=>DataObject(5,row)));
        changed.AddRange((await ReadCharacterMissionsAsync(connection,userId)).Select(row=>DataObject(96,row)));
        changed.AddRange((await ReadCostumesAsync(connection,userId)).Select(row=>DataObject(43,row)));
        // 同一十连中可能多次更新同一张海报或同一道具，客户端仅接收最终状态。
        return changed.Cast<object?[]>().GroupBy(row=>(Convert.ToInt32(row[0]),GachaRules.Long(((object?[])row[1]!)[0]))).Select(group=>(object?)group.Last()).ToArray();
    }
}
