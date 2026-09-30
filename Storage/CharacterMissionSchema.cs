using Npgsql;

static class CharacterMissionSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            create table if not exists user_character_mission_states (
              "userId" bigint not null references user_accounts(id) on delete cascade,
              character_base_master_id bigint not null,
              mission_master_id bigint not null,
              current_count bigint not null default 0 check(current_count >= 0),
              received_stage_order integer not null default 0 check(received_stage_order >= 0),
              updated_at timestamptz not null default now(),
              primary key("userId",character_base_master_id,mission_master_id)
            );
            alter table user_live_sessions add column if not exists character_mission_party bytea;
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
