using Npgsql;

static class CharacterGrowthSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            create table if not exists user_growth_bonuses (
                "userId" bigint primary key references user_accounts(id) on delete cascade,
                experience_bonus numeric not null default 0 check (experience_bonus >= 0)
            )
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
