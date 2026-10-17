using Npgsql;

static class MissionPassSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            create table if not exists user_mission_passes (
                "userId" bigint not null references user_accounts(id) on delete cascade,
                mission_pass_master_id bigint not null,
                free_phase integer not null default 0 check (free_phase between 0 and 50),
                sp_phase integer not null default 0 check (sp_phase between 0 and 50),
                free_loop_rewards integer not null default 0 check (free_loop_rewards >= 0),
                sp_loop_rewards integer not null default 0 check (sp_loop_rewards >= 0),
                updated_at timestamptz not null default now(),
                primary key ("userId", mission_pass_master_id)
            )
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
