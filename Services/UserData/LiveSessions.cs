using Npgsql;

sealed partial class UserDataService
{
    static async Task<LiveRewardRules.Snapshot?> ReadPendingLiveStartAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, long userId, byte[] hash)
    {
        await using var command = new NpgsqlCommand(
            """
            select start_snapshot from user_live_sessions v where v."userId"=$1
              and completion is null and start_hash=$2
              and not exists(select 1 from user_lesson_sessions l where l."userId"=v."userId" and l.started_at>v.started_at)
            """, connection, transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(hash);
        var value = await command.ExecuteScalarAsync();
        return value is byte[] bytes
            ? LiveRewardRules.Snapshot.FromWire((object?[])MsgPack.Decode(bytes)!)
            : null;
    }

    public async Task RetireLiveAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException("ACCOUNT_UNAUTHORIZED", 401);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        await ExecuteAsync(connection, transaction,
            "delete from user_live_sessions where \"userId\"=$1 and completion is null", userId);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "演出退出 operation=live-retire userId={UserId} outcome=success", userId);
    }
}
