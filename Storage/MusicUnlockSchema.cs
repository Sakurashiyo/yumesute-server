using Npgsql;

static class MusicUnlockSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            alter table user_live_music_states add column if not exists stella_released boolean not null default false;
            alter table user_live_music_states add column if not exists olivier_release_status integer not null default 0
              check (olivier_release_status between 0 and 3);
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}