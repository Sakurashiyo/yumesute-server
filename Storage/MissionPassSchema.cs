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
                -- SP 轨奖励必须凭购买凭据解锁；没有购买凭证的玩家即使总开关打开也不能领取。
                sp_purchased boolean not null default false,
                updated_at timestamptz not null default now(),
                primary key ("userId", mission_pass_master_id)
            )
            """, connection);
        await command.ExecuteNonQueryAsync();
        // 兼容既有存档：补列后默认未购买 SP。
        await using var alter = new NpgsqlCommand("""
            alter table user_mission_passes add column if not exists sp_purchased boolean not null default false
            """, connection);
        await alter.ExecuteNonQueryAsync();
    }
}
