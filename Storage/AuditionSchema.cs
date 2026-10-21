using Npgsql;

static class AuditionSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            create table if not exists user_audition_clears (
              id bigserial primary key,
              "userId" bigint not null references user_accounts(id) on delete cascade,
              audition_master_id bigint not null check (audition_master_id > 0),
              clear_phase integer not null default 0 check (clear_phase between 0 and 255),
              skip_clear_phase integer not null default 0 check (skip_clear_phase between 0 and clear_phase),
              unique ("userId", audition_master_id)
            );
            create table if not exists user_audition_parties (
              id bigserial primary key,
              "userId" bigint not null references user_accounts(id) on delete cascade,
              audition_master_id bigint not null check (audition_master_id > 0),
              phase integer not null check (phase between 0 and 255),
              score bigint not null check (score >= 0),
              snapshot bytea not null,
              unique ("userId", audition_master_id, phase)
            );
            create index if not exists user_audition_parties_ranking
              on user_audition_parties(audition_master_id, phase, score desc, id);
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
