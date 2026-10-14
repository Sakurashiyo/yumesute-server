using Npgsql;

sealed partial class UserDataService
{
    static async Task LockCircleAccountsAsync(NpgsqlConnection db, NpgsqlTransaction tx, params long[] ids)
    {
        await using var command = new NpgsqlCommand("select id from user_accounts where id=any($1) order by id for update", db, tx);
        command.Parameters.AddWithValue(ids.Distinct().ToArray());
        await command.ExecuteNonQueryAsync();
    }

    static async Task<CircleRow?> LockCircleAsync(NpgsqlConnection db, NpgsqlTransaction tx, string id)
    {
        await ExecuteAsync(db, tx, """
            select id from system_circles where id=$1 or display_id::text=$1 or previous_display_id::text=$1 for update
            """, id);
        return await ReadCircleAsync(db, id, tx);
    }

    static async Task<int> CircleAuthorityAsync(NpgsqlConnection db, NpgsqlTransaction? tx, string circle, long user)
    {
        await using var command = new NpgsqlCommand("select authority from user_circle_memberships where circle_id=$1 and \"userId\"=$2", db, tx);
        command.Parameters.AddWithValue(circle);
        command.Parameters.AddWithValue(user);
        return await command.ExecuteScalarAsync() is int authority ? authority : 0;
    }

    static object?[] CircleStatus(HttpContext context, long user, string operation, int status, bool success)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<UserDataService>>();
        if (success) logger.LogInformation("社团操作完成 operation={Operation} userId={UserId} resultStatus={Status}", operation, user, status);
        else logger.LogWarning("社团操作拒绝 operation={Operation} userId={UserId} errorCode={ErrorCode}", operation, user, status);
        return new object?[] { status };
    }

    public async Task<object?[]> EditCircleAsync(HttpContext context, object? body)
    {
        var user = await RequireAuthenticatedUserAsync(context);
        var payload = CircleProtocol.ReadPayload(body);
        if (payload is null || body is not object?[] values || values[0] is not string id)
            return CircleStatus(context,user,"edit",CircleProtocol.InvalidParameter,false);
        await using var db = await database.OpenConnectionAsync();
        await using var tx = await db.BeginTransactionAsync();
        var circle = await LockCircleAsync(db,tx,id);
        if (circle is null) return CircleStatus(context,user,"edit",CircleProtocol.DataNotFound,false);
        if (await CircleAuthorityAsync(db,tx,circle.StorageId,user)<2)
            return CircleStatus(context,user,"edit",CircleProtocol.RequestIllegal,false);
        await ExecuteAsync(db,tx,"""
            update system_circles set name=$2,comment=$3,play_time_start=$4,play_time_end=$5,entry_type=$6,
              member_type=$7,company_master_id=$8,character_base_master_id=$9 where id=$1
            """,circle.StorageId,payload.Name,payload.Comment,payload.Start,payload.End,payload.Entry,payload.Member,
            payload.Company is long company ? company : DBNull.Value, payload.CharacterBase is long character ? character : DBNull.Value);
        await tx.CommitAsync();
        return CircleStatus(context,user,"edit",CircleProtocol.EditSuccess,true);
    }

    public async Task<(object?[] Result, object?[] Present)> RemoveCircleMemberAsync(HttpContext context, string id, string? targetPublicId, bool release)
    {
        var user = await RequireAuthenticatedUserAsync(context);
        await using var db = await database.OpenConnectionAsync();
        long target = user;
        if (targetPublicId is not null)
        {
            await using var find = new NpgsqlCommand("select id from user_accounts where public_id=$1",db);
            find.Parameters.AddWithValue(targetPublicId);
            if (await find.ExecuteScalarAsync() is not long found)
                return (CircleStatus(context,user,"expulsion",CircleProtocol.DataNotFound,false),Array.Empty<object?>());
            target = found;
        }
        var operation = release ? "release" : targetPublicId is null ? "resignation" : "expulsion";
        await using var tx = await db.BeginTransactionAsync();
        await LockCircleAccountsAsync(db,tx,user,target);
        var circle = await LockCircleAsync(db,tx,id);
        if (circle is null) return (CircleStatus(context,user,operation,CircleProtocol.DataNotFound,false),Array.Empty<object?>());
        var authority = await CircleAuthorityAsync(db,tx,circle.StorageId,user);
        var targetAuthority = await CircleAuthorityAsync(db,tx,circle.StorageId,target);
        if (release ? circle.Owner != user : targetPublicId is null ? authority==0 || circle.Owner==user :
            target==user || authority<2 || targetAuthority==0 || targetAuthority>=authority)
            return (CircleStatus(context,user,operation,CircleProtocol.RequestIllegal,false),Array.Empty<object?>());
        if (release) await ExecuteAsync(db,tx,"delete from system_circles where id=$1",circle.StorageId);
        else await ExecuteAsync(db,tx,"delete from user_circle_memberships where circle_id=$1 and \"userId\"=$2",circle.StorageId,target);
        await tx.CommitAsync();
        var present = new object?[] { DataObject(0,await ReadUserAsync(db,user)) };
        return (CircleStatus(context,user,operation,release ? CircleProtocol.ReleaseSuccess : targetPublicId is null ?
            CircleProtocol.ResignationSuccess : CircleProtocol.ExpulsionSuccess,true),present);
    }

    public async Task<object?[]> ChangeCircleAuthorityAsync(HttpContext context, object? body)
    {
        var user = await RequireAuthenticatedUserAsync(context);
        if (body is not object?[] {Length:>=3} values || values[0] is not string id || values[1] is not string publicId ||
            CircleProtocol.Integer(values[2]) is not long next || next is <1 or >3)
            return CircleStatus(context,user,"authority",CircleProtocol.AuthorityPermissionDenied,false);
        await using var db = await database.OpenConnectionAsync();
        await using var find = new NpgsqlCommand("select id from user_accounts where public_id=$1",db);
        find.Parameters.AddWithValue(publicId);
        if (await find.ExecuteScalarAsync() is not long target)
            return CircleStatus(context,user,"authority",CircleProtocol.AuthorityDataNotFound,false);
        await using var tx = await db.BeginTransactionAsync();
        await LockCircleAccountsAsync(db,tx,user,target);
        var circle = await LockCircleAsync(db,tx,id);
        if (circle is null) return CircleStatus(context,user,"authority",CircleProtocol.AuthorityDataNotFound,false);
        var before = await CircleAuthorityAsync(db,tx,circle.StorageId,target);
        if (before==0) return CircleStatus(context,user,"authority",CircleProtocol.AuthorityDataNotFound,false);
        if (circle.Owner!=user || target==user)
            return CircleStatus(context,user,"authority",CircleProtocol.AuthorityPermissionDenied,false);
        if (next==2 && before!=2)
        {
            await using var count = new NpgsqlCommand("select count(*) from user_circle_memberships where circle_id=$1 and authority=2",db,tx);
            count.Parameters.AddWithValue(circle.StorageId);
            if ((long)(await count.ExecuteScalarAsync())!>=2)
                return CircleStatus(context,user,"authority",CircleProtocol.DeputyLeaderFull,false);
        }
        if (next==3)
        {
            await ExecuteAsync(db,tx,"update system_circles set \"ownerUserId\"=$2 where id=$1",circle.StorageId,target);
            await ExecuteAsync(db,tx,"update user_circle_memberships set authority=1,authority_update_type=2 where \"userId\"=$1",user);
        }
        await ExecuteAsync(db,tx,"update user_circle_memberships set authority=$2,authority_update_type=$3 where \"userId\"=$1",
            target,(int)next,next>before ? 1 : next<before ? 2 : 0);
        await tx.CommitAsync();
        return CircleStatus(context,user,"authority",next==3 ? CircleProtocol.LeaderChanged : CircleProtocol.AuthorityChanged,true);
    }

    public async Task<object?[]> SearchCirclesAsync(HttpContext context, string? name, object? body=null)
    {
        await RequireAuthenticatedUserAsync(context);
        var filter=body as object?[];
        if(body is not null && filter is not {Length:>=9}) throw new BadHttpRequestException(CircleProtocol.InvalidSearch);
        if(filter is not null && (Enumerable.Range(3,6).Any(index=>CircleProtocol.Integer(filter[index]) is null)
            || CircleProtocol.Integer(filter[3]) is < -1 or >29 || CircleProtocol.Integer(filter[4]) is < -1 or >29
            || CircleProtocol.Integer(filter[5]) is <0 or >2 || CircleProtocol.Integer(filter[6]) is <0 or >2
            || CircleProtocol.Integer(filter[7])<0 || CircleProtocol.Integer(filter[8])<0))
            throw new BadHttpRequestException(CircleProtocol.InvalidSearch);
        long Value(int index)=>filter is null ? -1 : CircleProtocol.Integer(filter[index])!.Value;
        await using var db=await database.OpenConnectionAsync();
        await using var query=new NpgsqlCommand("""
            select c.id from system_circles c where
              ($1='' or strpos(lower(c.name),lower($1))>0) and
              ($2<0 or c.play_time_start=$2) and ($3<0 or c.play_time_end=$3) and
              ($4<=0 or c.entry_type=$4) and
              ($5<=0 or $5=1 and (select count(*) from user_circle_memberships where circle_id=c.id)<=5
                or $5=2 and (select count(*) from user_circle_memberships where circle_id=c.id) between 6 and 9) and
              ($6 in (-1,0,999) or c.company_master_id=$6) and
              ($7 in (-1,0,999) or c.character_base_master_id=$7)
            order by c.created_at desc,c.id limit 50
            """,db);
        query.Parameters.AddWithValue(name ?? "");
        for(var index=3;index<=8;index++) query.Parameters.AddWithValue(Value(index));
        var ids=new List<string>();
        await using(var reader=await query.ExecuteReaderAsync())
            while(await reader.ReadAsync()) ids.Add(reader.GetString(0));
        var rows=new List<object?>();
        foreach(var id in ids)
        {
            var circle=await ReadCircleAsync(db,id);
            if(circle is not null) rows.Add(CircleInformation(circle));
        }
        return rows.ToArray();
    }
}
