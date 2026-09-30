using Npgsql;
using NpgsqlTypes;

sealed partial class UserDataService
{
    public async Task<(object?[] Result, object?[] Present)> CreateCircleAsync(HttpContext context, object? body)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        var payload = CircleProtocol.ReadPayload(body);
        if (payload is null) return CircleCreationFailure(context, userId, CircleProtocol.InvalidParameter);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // 锁定账号而非不存在的成员行，串行化同一玩家的并发创建。
        await ExecuteAsync(connection, transaction, "select id from user_accounts where id = $1 for update", userId);
        string? circleId;
        await using (var command = new NpgsqlCommand("""
            select circle_id from user_circle_memberships where "userId" = $1
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(userId);
            circleId = await command.ExecuteScalarAsync() as string;
        }
        if (circleId is not null)
        {
            var existing = await ReadCircleAsync(connection, circleId, transaction)
                ?? throw new InvalidOperationException("社团成员引用的社团不存在");
            if (existing.Owner != userId || existing.Payload != payload)
                return CircleCreationFailure(context, userId, CircleProtocol.AlreadyJoined);
        }
        else
        {
            circleId = Guid.NewGuid().ToString("N");
            await using var command = new NpgsqlCommand("""
                with created as (insert into system_circles
                  (id, "ownerUserId", name, comment, play_time_start, play_time_end, entry_type, member_type,
                   company_master_id, character_base_master_id)
                values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)
                on conflict (display_id) do nothing returning id)
                insert into user_circle_memberships ("userId", circle_id, authority) select $2,id,3 from created
                """, connection, transaction);
            command.Parameters.AddWithValue(circleId);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(payload.Name);
            command.Parameters.AddWithValue(payload.Comment);
            command.Parameters.AddWithValue(payload.Start);
            command.Parameters.AddWithValue(payload.End);
            command.Parameters.AddWithValue(payload.Entry);
            command.Parameters.AddWithValue(payload.Member);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)payload.Company ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)payload.CharacterBase ?? DBNull.Value });
            // 唯一索引仲裁不同账号间的随机碰撞；冲突时语句没有写入，可安全重试。
            var inserted = false;
            for (var attempt = 1; attempt <= 16; attempt++)
            {
                if (await command.ExecuteNonQueryAsync() == 1) { inserted = true; break; }
                context.RequestServices.GetRequiredService<ILogger<UserDataService>>()
                    .LogWarning("社团随机编号碰撞 userId={UserId} attempt={Attempt}", userId, attempt);
            }
            if (!inserted) throw new InvalidOperationException("社团随机编号重试耗尽");
        }
        var createdCircle = await ReadCircleAsync(connection, circleId, transaction)
            ?? throw new InvalidOperationException("创建后的社团不存在");
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>()
            .LogInformation("社团创建完成 operation={Operation} userId={UserId} circleId={CircleId} resultStatus={ResultStatus}",
                "circles-create", userId, createdCircle.Id, CircleProtocol.CreateSuccess);
        // 创建响应必须同步 User.CircleId，登录和返回主页也从同一成员表读取。
        return (new object?[] { CircleProtocol.CreateSuccess, createdCircle.Id },
            new object?[] { DataObject(0, await ReadUserAsync(connection, userId)) });
    }

    static (object?[] Result, object?[] Present) CircleCreationFailure(HttpContext context, long userId, int status)
    {
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>()
            .LogWarning("社团创建拒绝 operation={Operation} userId={UserId} errorCode={ErrorCode}", "circles-create", userId, status);
        return (new object?[] { status, null }, Array.Empty<object?>());
    }

    public async Task<object?[]> EditCircleBannerAsync(HttpContext context, object? body)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        var banner = CircleProtocol.ReadBanner(body, out var circleId);
        if (banner is null)
        {
            context.RequestServices.GetRequiredService<ILogger<UserDataService>>()
                .LogWarning("社团横幅参数非法 userId={UserId} errorCode={ErrorCode}", userId, CircleProtocol.InvalidParameter);
            return new object?[] { false };
        }
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            update system_circles set banner = $1
            where (id = $2 or display_id::text = $2 or previous_display_id::text = $2) and "ownerUserId" = $3
            """, connection);
        command.Parameters.AddWithValue(MsgPack.Encode(banner));
        command.Parameters.AddWithValue(circleId!);
        command.Parameters.AddWithValue(userId);
        var updated = await command.ExecuteNonQueryAsync() == 1;
        var logger = context.RequestServices.GetRequiredService<ILogger<UserDataService>>();
        if (updated) logger.LogInformation("社团横幅更新 userId={UserId} circleId={CircleId}", userId, circleId);
        else logger.LogWarning("社团横幅更新拒绝 userId={UserId} circleId={CircleId} errorCode={ErrorCode}",
            userId, circleId, CircleProtocol.RequestIllegal);
        return new object?[] { updated };
    }

    public async Task<object?[]> SetCircleRankingPublishedAsync(HttpContext context, string? value)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        if (!bool.TryParse(value, out var published) ||
            (context.Request.Query.TryGetValue("IsPublishRanking", out var queryValue) &&
             (!bool.TryParse(queryValue.ToString(), out var queried) || queried != published)))
            return new object?[] { CircleProtocol.InvalidParameter };
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            update system_circles set is_publish_ranking=$2 where "ownerUserId"=$1
            """, connection);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(published);
        if (await command.ExecuteNonQueryAsync() != 1)
        {
            context.RequestServices.GetRequiredService<ILogger<UserDataService>>()
                .LogWarning("社团排行榜设置拒绝 operation={Operation} userId={UserId} errorCode={ErrorCode}",
                    "circles-set-publish-ranking", userId, CircleProtocol.RequestIllegal);
            return new object?[] { CircleProtocol.RequestIllegal };
        }
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>()
            .LogInformation("社团排行榜设置完成 operation={Operation} userId={UserId} published={Published}",
                "circles-set-publish-ranking", userId, published);
        return new object?[] { CircleProtocol.EditSuccess };
    }

    public async Task<object?[]> GetCirclesAsync()
    {
        await using var connection = await database.OpenConnectionAsync();
        var ids = new List<string>();
        await using (var command = new NpgsqlCommand("select id from system_circles order by created_at desc, id limit 50", connection))
        {
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ids.Add(reader.GetString(0));
        }
        var results = new List<object?>();
        foreach (var id in ids)
        {
            // 删除与列表读取可能并发；已不存在的社团不再展示。
            var circle = await ReadCircleAsync(connection, id);
            if (circle is not null) results.Add(CircleInformation(circle));
        }
        return results.ToArray();
    }

    public async Task<object?[]> GetCircleAsync(string circleId)
    {
        await using var connection = await database.OpenConnectionAsync();
        var circle = await ReadCircleAsync(connection, circleId);
        return circle is null ? CircleInformationNotFound() : CircleInformation(circle);
    }

    public async Task<object?[]> GetMyCircleAsync(HttpContext context)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await using var connection = await database.OpenConnectionAsync();
        string? circleId;
        int authority = 0;
        DateTime staminaLastReceivedAt = CircleUnsetAt;
        await using (var command = new NpgsqlCommand("""
            select circle_id, authority, stamina_last_received_at from user_circle_memberships where "userId" = $1
            """, connection))
        {
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            circleId = await reader.ReadAsync() ? reader.GetString(0) : null;
            if (circleId is not null)
            {
                authority = reader.GetInt32(1);
                if (!reader.IsDBNull(2)) staminaLastReceivedAt = reader.GetDateTime(2);
            }
        }
        var circle = circleId is null ? null : await ReadCircleAsync(connection, circleId);
        if (circle is null)
            return new object?[] { null, "", "", -1, -1, 0, 0, 0, CircleProtocol.DataNotFound, null,
                null, null, GameResults.CircleSupportAndTheaterLevelInformation(), 0, 0, false, CircleUnsetAt, 0 };
        var p = circle.Payload;
        return new object?[] { circle.Id, p.Name, p.Comment, p.Start, p.End, p.Entry, circle.Count, authority,
            CircleProtocol.SearchSuccess, circle.Banner, p.Company, p.CharacterBase,
            GameResults.CircleSupportAndTheaterLevelInformation(), 0, 0, circle.IsPublishRanking, staminaLastReceivedAt, 0 };
    }

    public async Task<object?[]> GetCircleMembersAsync(string circleId)
    {
        await using var connection = await database.OpenConnectionAsync();
        var circle = await ReadCircleAsync(connection, circleId);
        if (circle is null) return new object?[] { Array.Empty<object?>(), CircleProtocol.DataNotFound, null };
        await using var command = new NpgsqlCommand("""
            select a.public_id, p.display_name, g.rank, g.last_login_at, m.authority,
                   coalesce(p.title_master_id,0), coalesce(p.home_character_master_id,110010),
                   p.is_home_character_illust, coalesce(p.icon_frame_master_id,190001)
            from user_circle_memberships m
            join user_accounts a on a.id = m."userId"
            join user_profiles p on p."userId" = a.id
            join user_game_states g on g."userId" = a.id
            where m.circle_id = $1 order by m.authority desc, m.joined_at, a.id
            """, connection);
        command.Parameters.AddWithValue(circle.StorageId);
        var members = new List<object?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            members.Add(new object?[] { reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetDateTime(3),
                reader.GetInt32(4), null, null, null, reader.GetInt64(5), 0L, 0L, reader.GetInt64(6),
                reader.GetBoolean(7), null, false, 0, reader.GetInt64(8) });
        return new object?[] { members.ToArray(), CircleProtocol.SearchSuccess, circle.Banner };
    }

    public async Task<object?[]> GetCircleJoinRequestsAsync(HttpContext context, string circleId)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await using var connection = await database.OpenConnectionAsync();
        var circle = await ReadCircleAsync(connection, circleId);
        if (circle is null) return new object?[] { Array.Empty<object?>(), CircleProtocol.DataNotFound, null };
        await using var command = new NpgsqlCommand("""
            select exists(select 1 from user_circle_memberships
              where circle_id = $1 and "userId" = $2 and authority >= 2)
            """, connection);
        command.Parameters.AddWithValue(circle.StorageId);
        command.Parameters.AddWithValue(userId);
        if (await command.ExecuteScalarAsync() is not true)
        {
            context.RequestServices.GetRequiredService<ILogger<UserDataService>>()
                .LogWarning("社团申请查询拒绝 userId={UserId} circleId={CircleId} errorCode={ErrorCode}",
                    userId, circle.Id, CircleProtocol.RequestIllegal);
            return new object?[] { Array.Empty<object?>(), CircleProtocol.RequestIllegal, null };
        }
        // 尚无入会申请写入功能，空列表表示无待处理申请，不伪造申请或审批成功。
        return new object?[] { Array.Empty<object?>(), CircleProtocol.SearchSuccess, circle.Banner };
    }

    static readonly DateTime CircleUnsetAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
    sealed record CircleRow(string Id, long Owner, CircleProtocol.Payload Payload, object? Banner,
        int Count, long Character, bool Awakening, long IconFrame, string StorageId, bool IsPublishRanking);

    static object?[] CircleInformation(CircleRow c)
    {
        var p = c.Payload;
        // key8 已弃用；保留空位，后续字段不能向前移动。
        return new object?[] { c.Id, p.Name, p.Comment, p.Start, p.End, p.Entry, c.Count, 0L, null,
            CircleProtocol.SearchSuccess, c.Banner, c.Character, c.Awakening, p.Company, p.CharacterBase, c.IconFrame };
    }

    static object?[] CircleInformationNotFound() => new object?[] { null, "", "", -1, -1, 0, 0, 0L, null,
        CircleProtocol.DataNotFound, null, 0L, false, null, null, 0L };

    static async Task<CircleRow?> ReadCircleAsync(NpgsqlConnection connection, string id, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("""
            select c.display_id::text, c."ownerUserId", c.name, c.comment, c.play_time_start, c.play_time_end,
                   c.entry_type, c.member_type, c.company_master_id, c.character_base_master_id, c.banner,
                   (select count(*)::int from user_circle_memberships m where m.circle_id = c.id),
                   coalesce(p.home_character_master_id,110010), p.is_home_character_illust,
                   coalesce(p.icon_frame_master_id,190001), c.id, c.is_publish_ranking
            from system_circles c join user_profiles p on p."userId" = c."ownerUserId"
            where c.id = $1 or c.display_id::text = $1 or c.previous_display_id::text = $1
            """, connection, transaction);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new(reader.GetString(0), reader.GetInt64(1),
            new(reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5),
                reader.GetInt32(6), reader.GetInt32(7), GetNullableInt64(reader, 8), GetNullableInt64(reader, 9)),
            reader.IsDBNull(10) ? null : MsgPack.Decode(reader.GetFieldValue<byte[]>(10)),
            reader.GetInt32(11), reader.GetInt64(12), reader.GetBoolean(13), reader.GetInt64(14),
            reader.GetString(15), reader.GetBoolean(16));
    }
}
