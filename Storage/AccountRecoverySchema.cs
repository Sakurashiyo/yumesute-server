using Npgsql;

static class AccountRecoverySchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            create table if not exists user_confirmation_codes (
              "userId" bigint primary key references user_accounts(id) on delete cascade,
              nonce text not null,
              expires_at timestamptz not null,
              consumed boolean not null default false,
              failed_attempts integer not null default 0 check (failed_attempts between 0 and 5)
            );
            create table if not exists user_account_recovery (
              "userId" bigint primary key references user_accounts(id) on delete cascade,
              linkage_code text not null unique check (linkage_code ~ '^[0-9]{10}$'),
              password_salt bytea not null check (octet_length(password_salt)=16),
              password_hash bytea not null check (octet_length(password_hash)=32),
              token_nonce text not null,
              claimed boolean not null default false,
              failed_attempts integer not null default 0 check (failed_attempts between 0 and 5),
              locked_until timestamptz,
              updated_at timestamptz not null default now()
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
