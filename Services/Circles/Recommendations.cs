using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> GetCircleFriendsAsync(HttpContext context)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        var mine = await GetMyCircleAsync(context);
        if (mine[0] is not string circleId) return new object?[] { Array.Empty<object?>(), CircleProtocol.DataNotFound, null };
        var authorized = await GetCircleJoinRequestsAsync(context, circleId);
        if (Convert.ToInt32(authorized[1]) != CircleProtocol.SearchSuccess) return authorized;
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            select a.public_id,p.display_name,g.rank,g.last_login_at,coalesce(p.title_master_id,0),
                coalesce(p.home_character_master_id,110010),p.is_home_character_illust,coalesce(p.icon_frame_master_id,190001)
            from user_friend_relationships f
            join user_accounts a on a.id=f."friendUserId"
            join user_profiles p on p."userId"=a.id
            join user_game_states g on g."userId"=a.id
            where f."userId"=$1 and p.is_public
              and not exists(select 1 from user_circle_memberships m where m."userId"=a.id)
            order by g.last_login_at desc,a.id limit 50
            """, connection);
        command.Parameters.AddWithValue(userId);
        var users = new List<object?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) users.Add(CircleSearchUser(reader));
        return new object?[] { users.ToArray(), CircleProtocol.SearchSuccess, authorized[2] };
    }

    public async Task<object?[]> GetCircleInvitingAsync(HttpContext context)
    {
        var mine = await GetMyCircleAsync(context);
        if (mine[0] is not string circleId) return new object?[] { Array.Empty<object?>(), CircleProtocol.DataNotFound, null };
        var authorized = await GetCircleJoinRequestsAsync(context, circleId);
        if (Convert.ToInt32(authorized[1]) != CircleProtocol.SearchSuccess) return authorized;
        // 尚无社团邀请写入；不能把推荐玩家误报为已邀请。
        return new object?[] { Array.Empty<object?>(), CircleProtocol.SearchSuccess, authorized[2] };
    }

    public async Task<object?[]> SearchCircleUserAsync(HttpContext context, string targetUserId)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        var mine = await GetMyCircleAsync(context);
        if (mine[0] is not string circleId) return new object?[] { null, CircleProtocol.DataNotFound };
        var authorized = await GetCircleJoinRequestsAsync(context, circleId);
        if (Convert.ToInt32(authorized[1]) != CircleProtocol.SearchSuccess)
            return new object?[] { null, authorized[1] };
        if (string.IsNullOrWhiteSpace(targetUserId)) return new object?[] { null, CircleProtocol.InvalidParameter };
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            select a.public_id,p.display_name,g.rank,g.last_login_at,coalesce(p.title_master_id,0),
                coalesce(p.home_character_master_id,110010),p.is_home_character_illust,coalesce(p.icon_frame_master_id,190001)
            from user_accounts a join user_profiles p on p."userId"=a.id
            join user_game_states g on g."userId"=a.id
            where a.public_id=$1 and a.id<>$2 and p.is_public
              and not exists(select 1 from user_circle_memberships m where m."userId"=a.id)
            """, connection);
        command.Parameters.AddWithValue(targetUserId.Trim());
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new object?[] { CircleSearchUser(reader), CircleProtocol.SearchSuccess }
            : new object?[] { null, CircleProtocol.DataNotFound };
    }

    static object?[] CircleSearchUser(NpgsqlDataReader reader) => new object?[]
    {
        reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetDateTime(3),
        null, null, null, null, reader.GetInt64(4), 0L, 0L, reader.GetInt64(5),
        reader.GetBoolean(6), null, false, 0, reader.GetInt64(7)
    };

    public async Task<object?[]> GetCircleRecommendedUsersAsync(HttpContext context,string circleId)
    {
        // 沿用申请列表的会话、社团存在性与管理员权限检查。
        var authorized = await GetCircleJoinRequestsAsync(context,circleId);
        if (Convert.ToInt32(authorized[1]) != CircleProtocol.SearchSuccess) return authorized;
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            select a.public_id,p.display_name,g.rank,g.last_login_at,coalesce(p.title_master_id,0),
                coalesce(p.home_character_master_id,110010),p.is_home_character_illust,coalesce(p.icon_frame_master_id,190001)
            from user_accounts a join user_profiles p on p."userId"=a.id join user_game_states g on g."userId"=a.id
            where p.is_public and not exists(select 1 from user_circle_memberships m where m."userId"=a.id)
            order by g.last_login_at desc,a.id limit 50
            """,connection);
        var users = new List<object?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) users.Add(CircleSearchUser(reader));
        return new object?[] {users.ToArray(),CircleProtocol.SearchSuccess,authorized[2]};
    }
}
