using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> GetAuditionInformationAsync(HttpContext context, long auditionId)
    {
        await RequireAuthenticatedUserAsync(context);
        var master = AuditionRules.Get(auditionId);
        await using var connection = await database.OpenConnectionAsync();
        // 官方选样策略无法由客户端确定；本服按阶段展示五支最高分真实队伍，保持稳定排序和有界响应。
        await using var command = new NpgsqlCommand("""
            select p.id,p.phase,p.score,p.snapshot from generate_series(1,$2) stages(phase)
            cross join lateral (
              select id,phase,score,snapshot from user_audition_parties
              where audition_master_id=$1 and phase=stages.phase order by score desc,id limit 5
            ) p order by p.phase,p.score desc,p.id
            """, connection);
        command.Parameters.AddWithValue(auditionId);
        command.Parameters.AddWithValue(master.MaxPhase);
        var groups = new SortedDictionary<int, List<object?>>();
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted))
        {
            var phase = reader.GetInt32(1);
            if (!groups.TryGetValue(phase, out var parties)) groups.Add(phase, parties = new List<object?>());
            parties.Add(AuditionPartyFromStorage(reader.GetInt64(0), reader.GetInt64(2), reader.GetFieldValue<byte[]>(3)));
        }
        return new object?[] { groups.Select(group => (object?)new object?[] { group.Value.ToArray(), group.Key }).ToArray() };
    }

    public async Task<object?[]?> GetAuditionHighScorePartyAsync(HttpContext context, long auditionId)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        _ = AuditionRules.Get(auditionId);
        await using var connection = await database.OpenConnectionAsync();
        return await ReadAuditionHighScorePartyAsync(connection, userId, auditionId);
    }

    static object?[] AuditionPartyFromStorage(long id, long score, byte[] bytes)
    {
        if (MsgPack.Decode(bytes) is not object?[] { Length: 11 } party || party[3] is not object?[] slots
            || slots.Length is < 1 or > 5 || slots.Any(slot => slot is not object?[] { Length: 15 }))
            throw new InvalidDataException("公演队伍快照格式错误");
        party[0] = id;
        party[1] = score;
        return party;
    }

    static async Task<object?[]?> ReadAuditionHighScorePartyAsync(NpgsqlConnection connection, long userId, long auditionId)
    {
        await using var command = new NpgsqlCommand("""
            select id,score,snapshot from user_audition_parties
            where "userId"=$1 and audition_master_id=$2 and phase=0
            """, connection);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(auditionId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? AuditionPartyFromStorage(reader.GetInt64(0), reader.GetInt64(1), reader.GetFieldValue<byte[]>(2)) : null;
    }

    static async Task<List<object?[]>> ReadAuditionClearsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand("""
            select c.id,c.audition_master_id,c.clear_phase,coalesce(p.id,0),a.public_id,c.skip_clear_phase
            from user_audition_clears c join user_accounts a on a.id=c."userId"
            left join user_audition_parties p on p."userId"=c."userId"
              and p.audition_master_id=c.audition_master_id and p.phase=0
            where c."userId"=$1 order by c.audition_master_id
            """, connection);
        command.Parameters.AddWithValue(userId);
        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new object?[] { reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.GetInt64(3), reader.GetString(4), reader.GetInt32(5) });
        return result;
    }
}
