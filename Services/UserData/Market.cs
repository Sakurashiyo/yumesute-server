using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    sealed record MarketState(DateTimeOffset RefreshedAt, int RefreshTimes, object?[][] Things);
    public sealed record MarketResponse(object?[] Result, object?[] Present);

    public async Task<MarketResponse> GetMarketAsync(HttpContext context, bool paidRefresh)
    {
        var userId=await RequireAuthenticatedUserAsync(context); await EnsureDefaultUserDataAsync(userId);
        await using var connection=await database.OpenConnectionAsync(); await using var tx=await connection.BeginTransactionAsync(); await LockLessonUserAsync(connection,tx,userId);
        var now=DateTimeOffset.UtcNow; var state=await ReadMarketStateAsync(connection,tx,userId); var present=new List<object?>();
        if (state is null || (!paidRefresh && state.RefreshedAt.AddDays(1)<=now)) state=NewMarketState(now,0);
        else if (paidRefresh) { if(state.RefreshTimes>=MarketRules.RefreshLimit) throw new BadHttpRequestException(MarketErrors.RefreshLimit,409); await PayJewelsAsync(connection,tx,userId); state=NewMarketState(now,state.RefreshTimes+1); present.Add(DataObject(128,await ReadCurrencyAsync(connection,userId))); }
        await SaveMarketStateAsync(connection,tx,userId,state); await tx.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation("随机商店刷新完成 operation={Operation} userId={UserId} refreshTimes={RefreshTimes} paid={Paid}", "market-refresh", userId, state.RefreshTimes, paidRefresh);
        return new(new object?[]{state.Things,state.RefreshTimes<MarketRules.RefreshLimit?MarketRules.RefreshCost:null},present.ToArray());
    }

    public async Task<MarketResponse> ExchangeMarketThingsAsync(HttpContext context, object? body)
    {
        if(body is not object?[] values || values.Length==0) throw new BadHttpRequestException(MarketErrors.InvalidRequest,400);
        var slots=values.Select(Convert.ToInt32).Distinct().ToArray(); if(slots.Any(x=>x<1||x>15)) throw new BadHttpRequestException(MarketErrors.InvalidRequest,400);
        var userId=await RequireAuthenticatedUserAsync(context); await EnsureDefaultUserDataAsync(userId); await using var c=await database.OpenConnectionAsync(); await using var tx=await c.BeginTransactionAsync(); await LockLessonUserAsync(c,tx,userId);
        var state=await ReadMarketStateAsync(c,tx,userId) ?? throw new BadHttpRequestException(MarketErrors.Unavailable,409); var rewards=new List<object?>();
        foreach(var slot in slots){var row=state.Things.Single(x=>Convert.ToInt32(x[0])==slot); if(Convert.ToBoolean(row[4])) throw new BadHttpRequestException(MarketErrors.Unavailable,409); var p=MarketRules.ProductsBySlot[slot].Single(x=>x.Id==Convert.ToInt64(row[1])); await PayCostAsync(c,tx,userId,p); if(p.ThingType==1) await UpsertItemQuantityAsync(c,tx,userId,p.ThingId,p.ThingQuantity); row[4]=true; rewards.Add(new object?[]{p.ThingType,p.ThingId,p.ThingQuantity,null,null,null,false});}
        await SaveMarketStateAsync(c,tx,userId,state); var present=new List<object?>{DataObject(128,await ReadCurrencyAsync(c,userId))}; await tx.CommitAsync(); context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation("随机商店购买完成 operation={Operation} userId={UserId} count={Count}", "market-exchange", userId, rewards.Count); return new(rewards.ToArray(),present.ToArray());
    }
    static MarketState NewMarketState(DateTimeOffset now,int refreshTimes)=>new(now,refreshTimes,Enumerable.Range(1,15).Select(slot=>{var p=MarketRules.ProductsBySlot[slot][Random.Shared.Next(MarketRules.ProductsBySlot[slot].Length)];return new object?[]{slot,p.Id,null,null,false,null};}).ToArray());
    async Task PayJewelsAsync(NpgsqlConnection c,NpgsqlTransaction tx,long uid){var x=await ReadCurrencyAsync(c,uid);var f=Convert.ToInt32(x[2]);var p=Convert.ToInt32(x[3]);if((long)f+p<20)throw new BadHttpRequestException(MarketErrors.Insufficient,409);var fc=Math.Min(f,20);await ExecuteAsync(c,tx,"update user_item_currencies set free_jewel=$2,paid_jewel=$3 where \"userId\"=$1",uid,f-fc,p-20+fc);await ExecuteAsync(c,tx,"update user_game_states set free_jewel=$2,paid_jewel=$3,updated_at=now() where \"userId\"=$1",uid,f-fc,p-20+fc);}
    async Task PayCostAsync(NpgsqlConnection c,NpgsqlTransaction tx,long uid,MarketRules.Product p){var n=p.RequiredQuantity;if(p.RequiredType==12){var coinCommand=new NpgsqlCommand("update user_item_currencies set coin=coin-$2 where \"userId\"=$1 and coin >= $2",c,tx); coinCommand.Parameters.AddWithValue(uid); coinCommand.Parameters.AddWithValue(n); if(await coinCommand.ExecuteNonQueryAsync()!=1)throw new BadHttpRequestException(MarketErrors.Insufficient,409);await ExecuteAsync(c,tx,"update user_game_states set coin=coin-$2,updated_at=now() where \"userId\"=$1",uid,n);}else if(p.RequiredType==13){var x=await ReadCurrencyAsync(c,uid);var f=Convert.ToInt32(x[2]);var j=Convert.ToInt32(x[3]);if((long)f+j<n)throw new BadHttpRequestException(MarketErrors.Insufficient,409);var fc=Math.Min(f,n);await ExecuteAsync(c,tx,"update user_item_currencies set free_jewel=$2,paid_jewel=$3 where \"userId\"=$1",uid,f-fc,j-n+fc);await ExecuteAsync(c,tx,"update user_game_states set free_jewel=$2,paid_jewel=$3,updated_at=now() where \"userId\"=$1",uid,f-fc,j-n+fc);}else if(p.RequiredType==1){if(await ExecuteMarketItemCostAsync(c,tx,uid,p.RequiredId,n)!=1)throw new BadHttpRequestException(MarketErrors.Insufficient,409);}else throw new InvalidOperationException($"随机商店消耗类型尚未实现：{p.RequiredType}");}
    static async Task<MarketState?> ReadMarketStateAsync(NpgsqlConnection c,NpgsqlTransaction tx,long uid){await using var cmd=new NpgsqlCommand("select last_refreshed_at,refresh_times,payload::text from user_market_states where \"userId\"=$1",c,tx);cmd.Parameters.AddWithValue(uid);await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())return null;using var j=JsonDocument.Parse(r.GetString(2));var rows=j.RootElement.EnumerateArray().Select(a=>a.EnumerateArray().Select(v=>v.ValueKind switch{JsonValueKind.Null=>null,JsonValueKind.True=>(object?)true,JsonValueKind.False=>false,JsonValueKind.Number when v.TryGetInt64(out var n)=>n,_=>throw new InvalidOperationException("随机商店存档字段非法")}).ToArray()).ToArray();return new(r.GetFieldValue<DateTimeOffset>(0),r.GetInt32(1),rows);}
    static async Task SaveMarketStateAsync(NpgsqlConnection c,NpgsqlTransaction tx,long uid,MarketState s){await using var cmd=new NpgsqlCommand("insert into user_market_states(\"userId\",last_refreshed_at,refresh_times,payload) values($1,$2,$3,$4::jsonb) on conflict(\"userId\") do update set last_refreshed_at=excluded.last_refreshed_at,refresh_times=excluded.refresh_times,payload=excluded.payload,updated_at=now()",c,tx);cmd.Parameters.AddWithValue(uid);cmd.Parameters.AddWithValue(s.RefreshedAt);cmd.Parameters.AddWithValue(s.RefreshTimes);cmd.Parameters.AddWithValue(JsonSerializer.Serialize(s.Things));await cmd.ExecuteNonQueryAsync();}
}
