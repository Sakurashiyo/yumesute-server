using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> CheckCircleJoinAsync(HttpContext context, string circleId)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await using var connection = await database.OpenConnectionAsync();
        var circle = await ReadCircleAsync(connection, circleId);
        return new object?[] { await CircleJoinStatusAsync(connection, null, userId, circle) };
    }

    static async Task<int> CircleJoinStatusAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        long userId, CircleRow? circle)
    {
        if (circle is null) return CircleProtocol.DataNotFound;
        await using var command = new NpgsqlCommand(
            "select exists(select 1 from user_circle_memberships where \"userId\"=$1)", connection, transaction);
        command.Parameters.AddWithValue(userId);
        if (await command.ExecuteScalarAsync() is true) return CircleProtocol.AlreadyJoined;
        if (circle.Count >= 30) return CircleProtocol.MemberAmountUpperLimit;
        return circle.Payload.Entry switch
        {
            1 => CircleProtocol.FreeEntry,
            2 => CircleProtocol.PreRequestSuccess,
            _ => CircleProtocol.RequestIllegal
        };
    }

    public async Task<(object?[] Result, object?[] Present)> JoinCircleAsync(HttpContext context, string circleId)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // 与创建社团使用相同账号锁；社团行锁串行化最后一个席位的竞争。
        await ExecuteAsync(connection, transaction, "select id from user_accounts where id=$1 for update", userId);
        await ExecuteAsync(connection, transaction, """
            select id from system_circles
            where id=$1 or display_id::text=$1 or previous_display_id::text=$1 for update
            """, circleId);
        var circle = await ReadCircleAsync(connection, circleId, transaction);
        int status;
        await using (var member = new NpgsqlCommand(
            "select circle_id from user_circle_memberships where \"userId\"=$1", connection, transaction))
        {
            member.Parameters.AddWithValue(userId);
            var existing = await member.ExecuteScalarAsync() as string;
            // 相同请求重试返回已完成的结果，其他社团不能覆盖现有关系。
            status = existing is not null && circle is not null && existing == circle.StorageId
                ? CircleProtocol.JoinSuccess : await CircleJoinStatusAsync(connection, transaction, userId, circle);
        }
        if (status == CircleProtocol.FreeEntry)
        {
            await ExecuteAsync(connection, transaction,
                "insert into user_circle_memberships (\"userId\",circle_id,authority) values ($1,$2,1)", userId, circle!.StorageId);
            status = CircleProtocol.JoinSuccess;
        }
        else if (status == CircleProtocol.PreRequestSuccess) status = CircleProtocol.RequestIllegal;
        var present = status == CircleProtocol.JoinSuccess
            ? new object?[] { DataObject(0, await ReadUserAsync(connection, userId)) } : Array.Empty<object?>();
        await transaction.CommitAsync();
        var logger = context.RequestServices.GetRequiredService<ILogger<UserDataService>>();
        if (status == CircleProtocol.JoinSuccess)
            logger.LogInformation("社团加入完成 userId={UserId} circleId={CircleId} resultStatus={ResultStatus}", userId, circleId, status);
        else logger.LogWarning("社团加入拒绝 userId={UserId} circleId={CircleId} errorCode={ErrorCode}", userId, circleId, status);
        return (new object?[] { status }, present);
    }
}
