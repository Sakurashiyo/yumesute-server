using Npgsql;

sealed partial class UserDataService
{
    public async Task<(object?[] Result, object?[] Present)> ReceiveCircleTheaterStaminaAsync(HttpContext context)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // 与捐赠和成员管理保持一致的锁顺序，避免领取体力与钱包更新互相等待。
        await LockCircleAccountsAsync(connection,transaction,userId);
        await LockLessonUserAsync(connection,transaction,userId);
        var circleId=await CircleIdForUserAsync(connection,transaction,userId);
        if(circleId is null) return (new object?[] { false }, Array.Empty<object?>());
        if(await LockCircleAsync(connection,transaction,circleId) is null)
            return (new object?[] { false }, Array.Empty<object?>());
        var dailyStamina=CircleSupportRules.TheaterDailyStamina(await ReadCircleSupportAsync(connection,circleId,transaction));
        DateTime? lastReceivedAt;
        await using (var command = new NpgsqlCommand("""
            select stamina_last_received_at from user_circle_memberships
            where "userId"=$1 for update
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return (new object?[] { false }, Array.Empty<object?>());
            lastReceivedAt = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
        }
        var now = DateTime.UtcNow;
        // 首次入社以入社时间起算，不能把「从未领取」当成无限累计白送 300 点。
        if (lastReceivedAt is null)
        {
            await ExecuteAsync(connection, transaction, """
                update user_circle_memberships set stamina_last_received_at=$2 where "userId"=$1
                """, userId, now);
            await transaction.CommitAsync();
            return (new object?[] { false }, Array.Empty<object?>());
        }
        // 客户端按完整小时累计：每日产量 × 完整小时 / 24；最多储存 300 点。
        var elapsedHours = Math.Max(0, (now - lastReceivedAt.Value).TotalHours);
        var amount = (int)Math.Min(300, Math.Floor(Math.Floor(elapsedHours) * dailyStamina / 24));
        if (amount == 0) return (new object?[] { false }, Array.Empty<object?>());
        await ExecuteAsync(connection, transaction, """
            update user_circle_memberships set stamina_last_received_at=$2 where "userId"=$1
            """, userId, now);
        await ExecuteAsync(connection, transaction, """
            update user_game_states set stamina=greatest(stamina, max_stamina) + least($2, greatest(max_stamina - stamina,0)), updated_at=now() where "userId"=$1
            """, userId, amount);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>()
            .LogInformation("社团剧场体力领取完成 operation={Operation} userId={UserId} amount={Amount}",
                "circles-receive-theater-stamina", userId, amount);
        return (new object?[] { true }, new object?[] { DataObject(0, await ReadUserAsync(connection, userId)) });
    }
}
