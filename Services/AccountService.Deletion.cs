using Npgsql;

sealed partial class AccountService
{
    // 对应客户端 AccountDeletionErrorTypes。
    const int DeletionErrorEnrolledCircle = 1;

    // 返回 AccountDeletionResult：[IsSuccess, Error]。
    public async Task<(object?[] Result, long UserId)> DeleteAccountAsync(string? apiToken)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var userId = await LockSessionAccountAsync(connection, transaction, apiToken);

        // 官方要求先退出剧团；剧团成员关系也承载会长归属，不能随账号级联删除。
        await using (var circle = new NpgsqlCommand("select 1 from user_circle_memberships where \"userId\"=$1 for update", connection, transaction))
        {
            circle.Parameters.AddWithValue(userId);
            if (await circle.ExecuteScalarAsync() is not null)
            {
                await transaction.RollbackAsync();
                return (new object?[] { false, DeletionErrorEnrolledCircle }, userId);
            }
        }

        foreach (var sql in new[]
        {
            "update system_invite_codes set \"createdBy\"=null where \"createdBy\"=$1",
            // 其余用户数据、会话与登录凭据均以 on delete cascade 引用 user_accounts。
            "delete from user_accounts where id=$1"
        })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(userId);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return (new object?[] { true, null }, userId);
    }
}
