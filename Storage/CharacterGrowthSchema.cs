using Npgsql;

static class CharacterGrowthSchema
{
    public static async Task MigrateAsync(NpgsqlConnection connection)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""
            create table if not exists user_growth_bonuses (
                "userId" bigint primary key references user_accounts(id) on delete cascade,
                experience_bonus numeric not null default 0 check (experience_bonus >= 0)
            );
            do $$ begin
                lock table user_character_cards in access exclusive mode;
                if not exists (select 1 from information_schema.columns
                    where table_schema = current_schema() and table_name = 'user_character_cards'
                      and column_name = 'display_awakening_status') then
                    alter table user_character_cards
                        add column display_awakening_status boolean not null default false;
                    -- 仅首次迁移恢复已觉醒卡面，后续启动保留玩家主动选择的原画。
                    update user_character_cards set display_awakening_status = true where awakening > 0;
                end if;
            end $$;
            alter table user_character_cards
                add column if not exists is_favorite boolean not null default false;
            """, connection, transaction);
        await command.ExecuteNonQueryAsync();
        // 清理旧版误开启的低稀有度卡面标记，保留觉醒阶段和四星卡的手动选择。
        await using var repair = new NpgsqlCommand("""
            update user_character_cards set display_awakening_status=false
            where display_awakening_status and (awakening=0 or not (character_master_id=any($1)))
            """, connection, transaction);
        repair.Parameters.AddWithValue(CharacterAwakeningRules.DisplayMasterIds);
        await repair.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
