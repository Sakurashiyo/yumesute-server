using Npgsql;

sealed partial class UserDataService
{
    static async Task<string?> CircleIdForUserAsync(NpgsqlConnection db,NpgsqlTransaction? tx,long user)
    {
        await using var query=new NpgsqlCommand("select circle_id from user_circle_memberships where \"userId\"=$1",db,tx);
        query.Parameters.AddWithValue(user);
        return await query.ExecuteScalarAsync() as string;
    }

    static async Task<object?[]> ReadCircleSupportAsync(NpgsqlConnection db,string circle,NpgsqlTransaction? tx=null)
    {
        var statuses=Enumerable.Range(1,4).Select(_=>new object?[] {0,0,CircleUnsetAt,CircleSupportRules.InitialLimit,
            new object?[] {0L,Array.Empty<object?>()}}).ToArray();
        await using(var query=new NpgsqlCommand("""
            select company,level,point,last_level_up_at,level_limit,donated_coin
            from system_circle_support where circle_id=$1 order by company
            """,db,tx))
        {
            query.Parameters.AddWithValue(circle);
            await using var reader=await query.ExecuteReaderAsync();
            while(await reader.ReadAsync()) statuses[reader.GetInt32(0)-1]=new object?[] {
                reader.GetInt32(1),reader.GetInt32(2),reader.GetDateTime(3),reader.GetInt32(4),
                new object?[] {reader.GetInt64(5),Array.Empty<object?>()}};
        }
        await using(var query=new NpgsqlCommand("select company,item_order,quantity from system_circle_support_donations where circle_id=$1 order by item_order",db,tx))
        {
            query.Parameters.AddWithValue(circle);
            var details=Enumerable.Range(1,4).Select(_=>new List<object?>()).ToArray();
            await using var reader=await query.ExecuteReaderAsync();
            while(await reader.ReadAsync()) details[reader.GetInt32(0)-1].Add(new object?[] {reader.GetInt32(1),reader.GetInt64(2)});
            for(var i=0;i<4;i++) ((object?[])statuses[i][4]!)[1]=details[i].ToArray();
        }
        return statuses.Cast<object?>().ToArray();
    }

    public async Task<object?[]> GetCircleSupportAsync(HttpContext context)
    {
        var user=await RequireAuthenticatedUserAsync(context);
        await using var db=await database.OpenConnectionAsync();
        var circle=await CircleIdForUserAsync(db,null,user);
        return circle is null ? GameResults.CircleSupportAndTheaterLevelInformation() : await ReadCircleSupportAsync(db,circle);
    }

    public async Task<object?[]> SetCircleSupportCompanyAsync(HttpContext context,string? value)
    {
        var user=await RequireAuthenticatedUserAsync(context);
        var parsed=CircleProtocol.ReadSupportCompany(value);
        if(parsed is not int company ||
            context.Request.Query.TryGetValue("company",out var query) && CircleProtocol.ReadSupportCompany(query.ToString())!=company)
            return CircleStatus(context,user,"support-company",CircleProtocol.InvalidParameter,false);
        await using var db=await database.OpenConnectionAsync();
        await using var update=new NpgsqlCommand("update user_circle_memberships set support_company=$2 where \"userId\"=$1",db);
        update.Parameters.AddWithValue(user); update.Parameters.AddWithValue(company);
        var saved=await update.ExecuteNonQueryAsync()==1;
        return CircleStatus(context,user,"support-company",saved ? CircleProtocol.EditSuccess : CircleProtocol.DataNotFound,saved);
    }

    public async Task<(object?[] Result,object?[] Present)> SetCircleActivityVisibilityAsync(HttpContext context,string? value)
    {
        var user=await RequireAuthenticatedUserAsync(context);
        if(!bool.TryParse(value,out var visible)) return (new object?[] {false},Array.Empty<object?>());
        await using var db=await database.OpenConnectionAsync();
        await using var command=new NpgsqlCommand("update user_profiles set is_public_activity_log=$2 where \"userId\"=$1",db);
        command.Parameters.AddWithValue(user); command.Parameters.AddWithValue(visible);
        await command.ExecuteNonQueryAsync();
        CircleStatus(context,user,"activity-visibility",CircleProtocol.EditSuccess,true);
        return (new object?[] {true},new object?[] {DataObject(1,await ReadProfileAsync(db,user))});
    }

    public async Task<(object?[] Result,object?[] Present)> DonateCircleSupportAsync(HttpContext context,object? body)
    {
        var user=await RequireAuthenticatedUserAsync(context);
        async Task<(object?[],object?[])> Failure()
        {
            CircleStatus(context,user,"support-donation",CircleProtocol.DonationError,false);
            return (new object?[] {await GetMyCircleAsync(context),CircleProtocol.DonationError},Array.Empty<object?>());
        }
        if(body is not object?[] {Length:>=4} values || CircleProtocol.Integer(values[0]) is not long company || company is <1 or >4 ||
            CircleProtocol.Integer(values[1]) is not long next || next is <11 or >int.MaxValue ||
            CircleProtocol.Integer(values[2]) is not long requestedCoin || requestedCoin is <0 or >int.MaxValue ||
            values[3] is not object?[] details) return await Failure();
        var requested=new Dictionary<int,long>();
        foreach(var detail in details)
        {
            if(detail is not object?[] {Length:2} row || CircleProtocol.Integer(row[0]) is not long order || order is <1 or >int.MaxValue ||
                CircleProtocol.Integer(row[1]) is not long quantity || quantity is <0 or >int.MaxValue || !requested.TryAdd((int)order,quantity))
                return await Failure();
        }
        if(requestedCoin==0 && requested.Values.All(n=>n==0)) return await Failure();
        await using var db=await database.OpenConnectionAsync();
        await using var tx=await db.BeginTransactionAsync();
        // 固定顺序：账号、玩家钱包、社团；共同捐赠串行化，超额部分不扣除。
        await LockCircleAccountsAsync(db,tx,user);
        await LockLessonUserAsync(db,tx,user);
        var circleId=await CircleIdForUserAsync(db,tx,user);
        if(circleId is null) return await Failure();
        var circle=await LockCircleAsync(db,tx,circleId);
        if(circle is null || await CircleAuthorityAsync(db,tx,circleId,user)==0) return await Failure();
        await ExecuteAsync(db,tx,"""
            insert into system_circle_support(circle_id,company,level_limit) values($1,$2,$3) on conflict do nothing
            """,circleId,(int)company,CircleSupportRules.InitialLimit);
        var state=(object?[])(await ReadCircleSupportAsync(db,circleId,tx))[(int)company-1]!;
        var limit=CircleSupportRules.NextLimit(Convert.ToInt32(state[3]),DateTime.UtcNow);
        if(limit is null || limit.Level!=next || requested.Keys.Any(order=>!limit.Materials.Any(material=>material.Order==order)))
            return await Failure();
        var totals=(object?[])state[4]!;
        var current=((object?[])totals[1]!).Cast<object?[]>().ToDictionary(row=>Convert.ToInt32(row[0]),row=>Convert.ToInt64(row[1]));
        var coin=Math.Min(requestedCoin,Math.Max(0,limit.Coin-Convert.ToInt64(totals[0])));
        var costs=limit.Materials.Select(material=>(Material:material,Quantity:Math.Min(requested.GetValueOrDefault(material.Order),
            Math.Max(0,material.Quantity-current.GetValueOrDefault(material.Order))))).Where(cost=>cost.Quantity>0).ToArray();
        await using(var wallet=new NpgsqlCommand("select coin from user_game_states where \"userId\"=$1",db,tx))
        {
            wallet.Parameters.AddWithValue(user);
            if(Convert.ToInt64(await wallet.ExecuteScalarAsync())<coin) return await Failure();
        }
        foreach(var cost in costs)
        {
            await using var inventory=new NpgsqlCommand("select quantity from user_item_possessions where \"userId\"=$1 and item_master_id=$2 for update",db,tx);
            inventory.Parameters.AddWithValue(user); inventory.Parameters.AddWithValue(cost.Material.ItemId);
            if(await inventory.ExecuteScalarAsync() is not int owned || owned<cost.Quantity) return await Failure();
        }
        if(coin>0)
        {
            // 两表都带余额守卫；任一失败即整笔回滚，避免钱包出现负数。
            await using (var wallet=new NpgsqlCommand("update user_game_states set coin=coin-$2,updated_at=now() where \"userId\"=$1 and coin >= $2",db,tx))
            {
                wallet.Parameters.AddWithValue(user); wallet.Parameters.AddWithValue(coin);
                if(await wallet.ExecuteNonQueryAsync()!=1) return await Failure();
            }
            await using (var wallet=new NpgsqlCommand("update user_item_currencies set coin=coin-$2 where \"userId\"=$1 and coin >= $2",db,tx))
            {
                wallet.Parameters.AddWithValue(user); wallet.Parameters.AddWithValue(coin);
                if(await wallet.ExecuteNonQueryAsync()!=1) return await Failure();
            }
            await ExecuteAsync(db,tx,"update system_circle_support set donated_coin=donated_coin+$3 where circle_id=$1 and company=$2",circleId,(int)company,coin);
        }
        var present=new List<object?>();
        foreach(var cost in costs)
        {
            var remaining=await ConsumeExperienceItemAsync(db,tx,user,cost.Material.ItemId,checked((int)cost.Quantity));
            var item=await ReadItemPossessionAsync(db,user,cost.Material.ItemId)
                ?? throw new InvalidOperationException("捐赠材料扣除后记录丢失");
            item[2]=remaining;
            present.Add(DataObject(27,item));
            await ExecuteAsync(db,tx,"""
                insert into system_circle_support_donations(circle_id,company,item_order,quantity) values($1,$2,$3,$4)
                on conflict(circle_id,company,item_order) do update set quantity=system_circle_support_donations.quantity+excluded.quantity
                """,circleId,(int)company,cost.Material.Order,cost.Quantity);
            current[cost.Material.Order]=current.GetValueOrDefault(cost.Material.Order)+cost.Quantity;
        }
        var unlocked=Convert.ToInt64(totals[0])+coin>=limit.Coin && limit.Materials.All(material=>current.GetValueOrDefault(material.Order)>=material.Quantity);
        if(unlocked)
        {
            await ExecuteAsync(db,tx,"update system_circle_support set level_limit=$3,donated_coin=0 where circle_id=$1 and company=$2",circleId,(int)company,limit.Level);
            await ExecuteAsync(db,tx,"delete from system_circle_support_donations where circle_id=$1 and company=$2",circleId,(int)company);
        }
        await tx.CommitAsync();
        present.Add(DataObject(0,await ReadUserAsync(db,user)));
        present.Add(DataObject(128,await ReadCurrencyAsync(db,user)));
        var status=coin<requestedCoin || costs.Sum(cost=>cost.Quantity)<requested.Values.Sum() ? CircleProtocol.DonationExceeded : CircleProtocol.DonationSuccess;
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "社团支援捐赠完成 userId={UserId} circleId={CircleId} company={Company} coin={Coin} unlocked={Unlocked} nextLimit={NextLimit}",
            user,circleId,company,coin,unlocked,next);
        return (new object?[] {await GetMyCircleAsync(context),status},present.ToArray());
    }
}
