using System.Text.Json;
using Npgsql;

static class GameOperationsSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection, LocalConfig? config = null)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        await using var schema = new NpgsqlCommand("""
            create table if not exists system_game_operations (
                id integer primary key check (id=1),
                revision bigint not null default 0 check (revision>=0),
                payload jsonb not null,
                updated_at timestamptz not null default now()
            );
            create table if not exists user_login_bonus_claims (
                "userId" bigint not null references user_accounts(id) on delete cascade,
                login_bonus_id bigint not null,
                game_day date not null,
                claimed_at timestamptz not null default now(),
                primary key ("userId",login_bonus_id,game_day)
            )
            """, connection, transaction);
        await schema.ExecuteNonQueryAsync();
        await using var defaults = new NpgsqlCommand("""
            insert into system_game_operations(id,payload) values(1,$1::jsonb) on conflict (id) do nothing returning id
            """, connection, transaction);
        var initial = GameOperationsConfiguration.Default();
        var resourcesRoot = (config ?? LocalConfig.FromEnvironment()).ResourcesRoot;
        foreach (var pool in GachaRules.AllPools)
            initial.Gachas[pool.Id] &= HistoricalGachaResources.Available(pool, resourcesRoot);
        defaults.Parameters.AddWithValue(JsonSerializer.Serialize(initial, GameOperationsJson.Options));
        var firstMigration = await defaults.ExecuteScalarAsync() is not null;
        // 只补充新支持的卡池，保留管理员已经保存的开关。
        await using var expand = new NpgsqlCommand("""
            update system_game_operations
            set payload=jsonb_set(payload,'{gachas}',($1::jsonb)||(payload->'gachas')),revision=revision+1,updated_at=now()
            where id=1 and not (payload->'gachas' ?& $2)
            """, connection, transaction);
        expand.Parameters.AddWithValue(JsonSerializer.Serialize(initial.Gachas,GameOperationsJson.Options));
        expand.Parameters.AddWithValue(initial.Gachas.Keys.Select(id => id.ToString()).ToArray());
        await expand.ExecuteNonQueryAsync();
        await using var history = new NpgsqlCommand("""
            insert into user_login_bonus_claims("userId",login_bonus_id,game_day)
            select "userId",split_part(payload->>'bootstrapKey','-',4)::bigint,
                to_date(split_part(payload->>'bootstrapKey','-',3),'YYYYMMDD')
            from user_item_inbox_packages
            where payload->>'bootstrapKey' ~ '^login-bonus-[0-9]{8}-[0-9]{1,18}$'
            on conflict ("userId",login_bonus_id,game_day) do nothing
            """, connection, transaction);
        await history.ExecuteNonQueryAsync();
        if (firstMigration)
        {
            // 旧礼物箱可能已清理，首次迁移仍以旧签到状态保留最后一次常规签到。
            await using var legacy = new NpgsqlCommand("""
                insert into user_login_bonus_claims("userId",login_bonus_id,game_day,claimed_at)
                select "userId",10006,((shown_at at time zone 'Asia/Tokyo')-interval '5 hours')::date,shown_at
                from user_login_bonus_states where status=1 and total_count>0
                on conflict ("userId",login_bonus_id,game_day) do nothing
                """, connection, transaction);
            await legacy.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }
}
