using Npgsql;

static class AnotherNotationSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            create table if not exists user_another_notation_results (
              id bigint primary key,
              "userId" bigint not null references user_accounts(id) on delete cascade,
              another_notation_master_id bigint not null check (another_notation_master_id > 0),
              clear_lamp integer not null default 0 check (clear_lamp between 0 and 9),
              rate_grade integer not null default 0 check (rate_grade between 0 and 9),
              achievement_rate numeric not null default 0 check (achievement_rate between 0 and 101),
              unique ("userId", another_notation_master_id)
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
