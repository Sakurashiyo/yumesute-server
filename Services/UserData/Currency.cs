using Npgsql;

sealed partial class UserDataService
{
    // 货币以 user_item_currencies 为准；user_game_states 只保存从表同步值。
    // 所有写入必须同时更新两表，读取一律走 ReadCurrencyAsync，避免同一账号出现两个余额。
    static async Task SetJewelsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, int freeJewel, int paidJewel)
    {
        if (freeJewel < 0 || paidJewel < 0) throw new InvalidOperationException("宝石余额不能为负");
        await ExecuteAsync(connection, transaction,
            "update user_item_currencies set free_jewel=$2,paid_jewel=$3 where \"userId\"=$1", userId, freeJewel, paidJewel);
        await ExecuteAsync(connection, transaction,
            "update user_game_states set free_jewel=$2,paid_jewel=$3,updated_at=now() where \"userId\"=$1", userId, freeJewel, paidJewel);
    }

    static async Task<object?[]> ReadWalletAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        await using var command = new NpgsqlCommand(
            "select coin, free_jewel, paid_jewel from user_game_states where \"userId\"=$1", connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new object?[] { userId, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2) };
    }

    static async Task AddCurrencyIncrementAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId,
        string column, int amount)
    {
        if (amount < 0) throw new InvalidOperationException("货币增量不能为负");
        if (column is not ("coin" or "free_jewel" or "paid_jewel")) throw new InvalidOperationException($"不支持的货币列：{column}");
        await ExecuteAsync(connection, transaction,
            $"update user_item_currencies set {column}={column}+$2 where \"userId\"=$1", userId, amount);
        await ExecuteAsync(connection, transaction,
            $"update user_game_states set {column}={column}+$2,updated_at=now() where \"userId\"=$1", userId, amount);
    }
}
