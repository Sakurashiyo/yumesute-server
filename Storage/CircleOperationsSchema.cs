using Npgsql;

static class CircleOperationsSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            alter table user_circle_memberships add column if not exists support_company integer not null default 0;
            alter table user_circle_memberships add column if not exists authority_update_type integer not null default 0;
            alter table user_profiles add column if not exists is_public_activity_log boolean not null default true;
            create table if not exists system_circle_entries (
              id bigint generated always as identity primary key,
              circle_id text not null references system_circles(id) on delete cascade,
              "userId" bigint not null references user_accounts(id) on delete cascade,
              kind integer not null check(kind in (1,2)),
              status integer not null default 0 check(status between 0 and 3),
              created_at timestamptz not null default now(),
              unique(circle_id,"userId",kind)
            );
            create index if not exists system_circle_entries_user_idx on system_circle_entries("userId",kind,status);
            create table if not exists system_circle_support (
              circle_id text not null references system_circles(id) on delete cascade,
              company integer not null check(company between 1 and 4),
              level integer not null default 0 check(level>=0),
              point integer not null default 0 check(point>=0),
              level_limit integer not null default 10 check(level_limit>=10),
              last_level_up_at timestamptz not null default '0001-01-01 UTC',
              donated_coin bigint not null default 0 check(donated_coin>=0),
              primary key(circle_id,company)
            );
            create table if not exists system_circle_support_donations (
              circle_id text not null,
              company integer not null,
              item_order integer not null,
              quantity bigint not null check(quantity>=0),
              primary key(circle_id,company,item_order),
              foreign key(circle_id,company) references system_circle_support(circle_id,company) on delete cascade
            );
            create table if not exists user_circle_support_points (
              "userId" bigint not null references user_accounts(id) on delete cascade,
              circle_id text not null references system_circles(id) on delete cascade,
              support_day date not null,
              point integer not null check(point between 0 and 10000),
              primary key("userId",circle_id,support_day)
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
