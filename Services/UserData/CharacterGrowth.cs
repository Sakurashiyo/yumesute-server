using Npgsql;

sealed partial class UserDataService
{
    static async Task NormalizeCharacterGrowthAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        await using var insert = new NpgsqlCommand("insert into user_growth_bonuses (\"userId\") values ($1) on conflict do nothing", connection, transaction);
        insert.Parameters.AddWithValue(userId);
        await insert.ExecuteNonQueryAsync();
    }
    static async Task<object?[]> ReadGrowthBonusAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand("select experience_bonus from user_growth_bonuses where \"userId\"=$1", connection);
        command.Parameters.AddWithValue(userId);
        var value = await command.ExecuteScalarAsync();
        if (value is not decimal bonus) throw new InvalidOperationException("账号成长加成数据缺失");
        return DataObject(67, new object?[] { userId, (float)bonus, 0f });
    }
}
