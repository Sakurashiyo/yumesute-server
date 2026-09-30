using Npgsql;

sealed partial class UserDataService
{
    public async Task<(object?[] Result, object?[] Present)> ReceiveCircleTheaterStaminaAsync(HttpContext context)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
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
        // 剧场每小时产生 4.17 点、最多保留 300 点；首次加入时视为满额。
        var elapsedHours = lastReceivedAt is null ? double.MaxValue : Math.Max(0, (now - lastReceivedAt.Value).TotalHours);
        var amount = Math.Min(300, (int)Math.Floor(elapsedHours * 4.17));
        if (amount == 0) return (new object?[] { false }, Array.Empty<object?>());
        await ExecuteAsync(connection, transaction, """
            update user_circle_memberships set stamina_last_received_at=$2 where "userId"=$1
            """, userId, now);
        await ExecuteAsync(connection, transaction, """
            update user_game_states set stamina=stamina+$2, updated_at=now() where "userId"=$1
            """, userId, amount);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>()
            .LogInformation("社团剧场体力领取完成 operation={Operation} userId={UserId} amount={Amount}",
                "circles-receive-theater-stamina", userId, amount);
        return (new object?[] { true }, new object?[] { DataObject(0, await ReadUserAsync(connection, userId)) });
    }
}
