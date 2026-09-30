using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

sealed partial class AdminSaveService(PostgresDatabase database, AdminSaveCatalog catalog)
{
    public async Task<object[]> ListUsersAsync(string search)
    {
        if (search.Length > 100) throw new AdminSaveException(AdminSaveErrors.Invalid, "搜索条件过长");
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select a.id, a.public_id, p.display_name, g.rank from user_accounts a
            join user_profiles p on p."userId" = a.id join user_game_states g on g."userId" = a.id
            where $1 = '' or position(lower($1) in lower(p.display_name)) > 0
              or position($1 in a.public_id) > 0 or a.id::text = $1
            order by a.id desc limit 100
            """, connection);
        command.Parameters.AddWithValue(search.Trim());
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(new { id = reader.GetInt64(0).ToString(), publicId = reader.GetString(1), name = reader.GetString(2), rank = reader.GetInt32(3) });
        return rows.ToArray();
    }

    public async Task<AdminSaveState> ReadAsync(long userId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var state = await ReadStateAsync(connection, transaction, userId);
        await transaction.CommitAsync();
        return state;
    }

    static async Task<AdminSaveState> ReadStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        string publicId, name;
        AdminPlayerStats stats;
        await using (var command = Command(connection, transaction,
            """
            select a.public_id, p.display_name, g.rank, g.exp, g.rank_limit, g.stamina, c.free_jewel, c.paid_jewel, c.coin
            from user_accounts a join user_profiles p on p."userId" = a.id
            join user_game_states g on g."userId" = a.id join user_item_currencies c on c."userId" = a.id
            where a.id = $1
            """, userId))
        {
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new AdminSaveException(AdminSaveErrors.NotFound, "用户不存在或尚未初始化存档", 404);
            publicId = reader.GetString(0); name = reader.GetString(1);
            stats = new(reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8));
        }
        var cards = new List<AdminOwnedCard>();
        await using (var command = Command(connection, transaction, "select id, character_master_id, level, awakening, is_locked from user_character_cards where \"userId\" = $1 order by character_master_id", userId))
        {
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) cards.Add(new(reader.GetInt64(0).ToString(), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetBoolean(4)));
        }
        var costumes = new List<AdminOwnedCostume>();
        await using (var command = Command(connection, transaction, "select costume_master_id from user_character_costumes where \"userId\" = $1 order by costume_master_id", userId))
        {
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) costumes.Add(new(reader.GetInt64(0)));
        }
        var items = new List<AdminOwnedItem>();
        await using (var command = Command(connection, transaction, "select item_master_id, quantity from user_item_possessions where \"userId\" = $1 order by item_master_id", userId))
        {
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) items.Add(new(reader.GetInt64(0), reader.GetInt32(1)));
        }
        var state = new AdminSaveState(userId.ToString(), publicId, name, "", stats, cards.ToArray(), costumes.ToArray(), items.ToArray());
        return state with { Revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state))) };
    }

    public async Task<AdminSaveState> SaveAsync(long userId, AdminSaveCommand request)
    {
        if (request.Revision is null || request.Revision.Length != 64 || request.Kind is not ("stats" or "card" or "costume" or "item"))
            throw new AdminSaveException(AdminSaveErrors.Invalid, "无效的存档操作");
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        // 所有管理操作先锁定同一玩家，防止两次管理写入交错。
        await using (var command = Command(connection, transaction, "select \"userId\" from user_game_states where \"userId\" = $1 for update", userId))
            if (await command.ExecuteScalarAsync() is null) throw new AdminSaveException(AdminSaveErrors.NotFound, "用户不存在", 404);
        var current = await ReadStateAsync(connection, transaction, userId);
        if (current.Revision != request.Revision) throw new AdminSaveException(AdminSaveErrors.Conflict, "存档已发生变化，请刷新后重试", 409);
        switch (request.Kind)
        {
            case "stats": await SaveStatsAsync(connection, transaction, userId, request.Stats); break;
            case "card": await SaveCardAsync(connection, transaction, userId, request); break;
            case "costume": await SaveCostumeAsync(connection, transaction, userId, request); break;
            case "item": await SaveItemAsync(connection, transaction, userId, request); break;
        }
        var updated = await ReadStateAsync(connection, transaction, userId);
        await transaction.CommitAsync();
        return updated;
    }

    static async Task SaveStatsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, AdminPlayerStats? stats)
    {
        if (stats is null || stats.Rank is < 1 or > 300 || stats.RankLimit < stats.Rank || stats.RankLimit > 300 || stats.Exp < 0 ||
            new[] { stats.Stamina, stats.FreeJewel, stats.PaidJewel, stats.Coin }.Any(value => value is < 0 or > 1000000000))
            throw new AdminSaveException(AdminSaveErrors.Invalid, "等级范围 1–300，等级不能高于上限；体力和货币范围 0–10 亿");
        var progress = PlayerProgressionRules.Advance(stats.Rank, stats.Exp, 0, stats.RankLimit);
        if (progress.Rank != stats.Rank || progress.Exp != stats.Exp)
            throw new AdminSaveException(AdminSaveErrors.Invalid, "经验不能达到当前等级的升级门槛；请直接提高等级或降低经验");
        await ExecuteAsync(connection, transaction,
            """
            update user_game_states set rank = $2, exp = $3, rank_limit = $4, max_stamina = $5,
              stamina = $6, free_jewel = $7, paid_jewel = $8, coin = $9, updated_at = now() where "userId" = $1
            """, userId, stats.Rank, stats.Exp, stats.RankLimit, progress.MaxStamina, stats.Stamina, stats.FreeJewel, stats.PaidJewel, stats.Coin);
        await ExecuteAsync(connection, transaction, "update user_item_currencies set free_jewel = $2, paid_jewel = $3, coin = $4 where \"userId\" = $1", userId, stats.FreeJewel, stats.PaidJewel, stats.Coin);
    }

    static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params object[] values)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return command;
    }
    static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params object[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        await command.ExecuteNonQueryAsync();
    }
    static long NewId() => Random.Shared.NextInt64(1, long.MaxValue);
}
