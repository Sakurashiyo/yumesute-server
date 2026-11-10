using Npgsql;

static class PhotoSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            create sequence if not exists local_photo_id_seq start 40000000;
            create table if not exists user_photo_media (
              photo_id bigint primary key references user_photo_records(id) on delete cascade,
              "userId" bigint not null references user_accounts(id) on delete cascade,
              request_hash text not null, image bytea not null, thumbnail bytea not null,
              row_payload bytea not null,
              unique("userId",request_hash)
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
