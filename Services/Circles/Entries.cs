using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> SendCircleEntryAsync(HttpContext context, string value, bool invitation)
    {
        var user = await RequireAuthenticatedUserAsync(context);
        await using var db = await database.OpenConnectionAsync();
        long target = user;
        string? circleId = value;
        if (invitation)
        {
            await using var query = new NpgsqlCommand("""
                select a.id,(select circle_id from user_circle_memberships where "userId"=$1)
                from user_accounts a where a.public_id=$2
                """,db);
            query.Parameters.AddWithValue(user);
            query.Parameters.AddWithValue(value);
            await using var reader = await query.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.IsDBNull(1)) return new object?[] { CircleProtocol.DataNotFound,null };
            target=reader.GetInt64(0); circleId=reader.GetString(1);
        }
        await using var tx = await db.BeginTransactionAsync();
        await LockCircleAccountsAsync(db,tx,user,target);
        var circle = await LockCircleAsync(db,tx,circleId);
        int status;
        long? entryId=null;
        if (circle is null) status=CircleProtocol.DataNotFound;
        else if (invitation && await CircleAuthorityAsync(db,tx,circle.StorageId,user)<2) status=CircleProtocol.RequestIllegal;
        else
        {
            status=await CircleJoinStatusAsync(db,tx,target,circle);
            if (status is CircleProtocol.FreeEntry or CircleProtocol.PreRequestSuccess || invitation && status==CircleProtocol.RequestIllegal)
            {
                var kind=invitation ? 1 : 2;
                // 新一轮申请使用新编号，迟到的旧审批不能批准后来重新提交的申请。
                await ExecuteAsync(db,tx,"delete from system_circle_entries where circle_id=$1 and \"userId\"=$2 and kind=$3 and status<>0",
                    circle!.StorageId,target,kind);
                await using var insert = new NpgsqlCommand("""
                    insert into system_circle_entries(circle_id,"userId",kind) values($1,$2,$3)
                    on conflict(circle_id,"userId",kind) do nothing returning id
                    """,db,tx);
                insert.Parameters.AddWithValue(circle!.StorageId); insert.Parameters.AddWithValue(target); insert.Parameters.AddWithValue(kind);
                if (await insert.ExecuteScalarAsync() is long id)
                {
                    entryId=id;
                    status=invitation ? CircleProtocol.InviteSuccess : CircleProtocol.RequestSuccess;
                }
                else
                {
                    await using var existing = new NpgsqlCommand("select id from system_circle_entries where circle_id=$1 and \"userId\"=$2 and kind=$3",db,tx);
                    existing.Parameters.AddWithValue(circle.StorageId); existing.Parameters.AddWithValue(target); existing.Parameters.AddWithValue(kind);
                    entryId=(long)(await existing.ExecuteScalarAsync())!;
                    status=invitation ? CircleProtocol.AlreadyInvite : CircleProtocol.AlreadyRequest;
                }
            }
        }
        await tx.CommitAsync();
        CircleStatus(context,user,invitation ? "invite" : "join-request",status,status is CircleProtocol.InviteSuccess or CircleProtocol.RequestSuccess);
        return invitation ? new object?[] {status,entryId} : new object?[] {status};
    }

    public async Task<(object?[] Result,object?[] Present)> ProcessCircleEntryAsync(HttpContext context, long id, bool invitation, string action,string? expectedId=null)
    {
        var user=await RequireAuthenticatedUserAsync(context);
        if(expectedId is not null && (!long.TryParse(expectedId,out var queried) || queried!=id))
            return (CircleStatus(context,user,action,CircleProtocol.InvalidParameter,false),Array.Empty<object?>());
        await using var db=await database.OpenConnectionAsync();
        long target; string circleId;
        await using (var query=new NpgsqlCommand("select \"userId\",circle_id from system_circle_entries where id=$1 and kind=$2",db))
        {
            query.Parameters.AddWithValue(id); query.Parameters.AddWithValue(invitation ? 1 : 2);
            await using var reader=await query.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return (CircleStatus(context,user,action,CircleProtocol.DataNotFound,false),Array.Empty<object?>());
            target=reader.GetInt64(0); circleId=reader.GetString(1);
        }
        await using var tx=await db.BeginTransactionAsync();
        await LockCircleAccountsAsync(db,tx,user,target);
        var circle=await LockCircleAsync(db,tx,circleId);
        if (circle is null) return (CircleStatus(context,user,action,CircleProtocol.DataNotFound,false),Array.Empty<object?>());
        var authority=await CircleAuthorityAsync(db,tx,circleId,user);
        // 申请由管理员审批；邀请由被邀请人接受/拒绝，取消邀请属于管理员。
        if (invitation && action!="cancel" ? user!=target : authority<2)
            return (CircleStatus(context,user,action,CircleProtocol.RequestIllegal,false),Array.Empty<object?>());
        int current;
        await using (var query=new NpgsqlCommand("select status from system_circle_entries where id=$1 for update",db,tx))
        {
            query.Parameters.AddWithValue(id);
            if (await query.ExecuteScalarAsync() is not int found)
                return (CircleStatus(context,user,action,CircleProtocol.DataNotFound,false),Array.Empty<object?>());
            current=found;
        }
        var next=action=="approve" ? 1 : action=="cancel" ? 3 : 2;
        if (current!=0 && current!=next)
            return (CircleStatus(context,user,action,CircleProtocol.RequestIllegal,false),Array.Empty<object?>());
        if (next==1 && current==0)
        {
            await using var membership=new NpgsqlCommand("select circle_id from user_circle_memberships where \"userId\"=$1",db,tx);
            membership.Parameters.AddWithValue(target);
            if (await membership.ExecuteScalarAsync() is string)
                return (CircleStatus(context,user,action,CircleProtocol.AlreadyJoined,false),Array.Empty<object?>());
            if (circle.Count>=30) return (CircleStatus(context,user,action,CircleProtocol.MemberAmountUpperLimit,false),Array.Empty<object?>());
            await ExecuteAsync(db,tx,"insert into user_circle_memberships(\"userId\",circle_id,authority) values($1,$2,1)",target,circleId);
            await ExecuteAsync(db,tx,"update system_circle_entries set status=2 where \"userId\"=$1 and status=0 and id<>$2",target,id);
        }
        if (current==0) await ExecuteAsync(db,tx,"update system_circle_entries set status=$2 where id=$1",id,next);
        await tx.CommitAsync();
        var present=user==target && next==1 ? new object?[] {DataObject(0,await ReadUserAsync(db,user))} : Array.Empty<object?>();
        return (CircleStatus(context,user,action,next==1 ? CircleProtocol.ApproveSuccess : next==3 ? CircleProtocol.CancelSuccess : CircleProtocol.RejectSuccess,true),present);
    }

    public async Task<object?[]> GetInvitedCirclesAsync(HttpContext context)
    {
        var user=await RequireAuthenticatedUserAsync(context);
        await using var db=await database.OpenConnectionAsync();
        var ids=new List<(string Circle,long Invite)>();
        await using (var query=new NpgsqlCommand("""
            select circle_id,id from system_circle_entries where "userId"=$1 and kind=1 and status=0 order by id desc limit 100
            """,db))
        {
            query.Parameters.AddWithValue(user);
            await using var reader=await query.ExecuteReaderAsync();
            while(await reader.ReadAsync()) ids.Add((reader.GetString(0),reader.GetInt64(1)));
        }
        var rows=new List<object?>();
        foreach(var (id,invite) in ids)
        {
            var circle=await ReadCircleAsync(db,id);
            if(circle is not null) {var row=CircleInformation(circle); row[7]=invite; rows.Add(row); }
        }
        return rows.ToArray();
    }

    async Task<object?[]> ReadCircleEntriesAsync(HttpContext context,string publicCircleId,bool invitation)
    {
        var user=await RequireAuthenticatedUserAsync(context);
        await using var db=await database.OpenConnectionAsync();
        var circle=await ReadCircleAsync(db,publicCircleId);
        if(circle is null) return new object?[] {Array.Empty<object?>(),CircleProtocol.DataNotFound,null};
        if(await CircleAuthorityAsync(db,null,circle.StorageId,user)<2)
            return new object?[] {Array.Empty<object?>(),CircleProtocol.RequestIllegal,null};
        await using var query=new NpgsqlCommand("""
            select a.public_id,p.display_name,g.rank,g.last_login_at,coalesce(p.title_master_id,0),
              coalesce(p.home_character_master_id,110010),p.is_home_character_illust,coalesce(p.icon_frame_master_id,190001),e.id
            from system_circle_entries e join user_accounts a on a.id=e."userId"
            join user_profiles p on p."userId"=a.id join user_game_states g on g."userId"=a.id
            where e.circle_id=$1 and e.kind=$2 and e.status=0
              and not exists(select 1 from user_circle_memberships m where m."userId"=a.id)
            order by e.id desc limit 100
            """,db);
        query.Parameters.AddWithValue(circle.StorageId); query.Parameters.AddWithValue(invitation ? 1 : 2);
        var rows=new List<object?>();
        await using var reader=await query.ExecuteReaderAsync();
        while(await reader.ReadAsync())
        {
            var row=CircleSearchUser(reader);
            row[invitation ? 6 : 7]=reader.GetInt64(8);
            rows.Add(row);
        }
        return new object?[] {rows.ToArray(),CircleProtocol.SearchSuccess,circle.Banner};
    }
}
