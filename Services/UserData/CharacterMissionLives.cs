using Npgsql;

sealed partial class UserDataService
{
    static async Task<object?[]> ReadMissionPartyAsync(NpgsqlConnection connection, long userId, long partyId)
    {
        await using var command = new NpgsqlCommand("""
            select b.character_base_master_id,bool_or(s.slot_number=p.party_type)
            from user_party_groups p join user_party_slots s on s.party_id=p.id and s."userId"=p."userId"
            join user_character_cards c on c.id=s.character_card_id and c."userId"=p."userId"
            join user_character_bases b on b.id=c.character_base_id and b."userId"=p."userId"
            where p.id=$1 and p."userId"=$2 group by b.character_base_master_id order by b.character_base_master_id
            """,connection);
        command.Parameters.AddWithValue(partyId);
        command.Parameters.AddWithValue(userId);
        var result = new List<object?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new object?[] {reader.GetInt64(0),reader.GetBoolean(1)});
        return result.ToArray();
    }

    static async Task RecordCharacterMissionLiveAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,long userId,object?[] party,object?[] finish,bool cleared)
    {
        var acts = finish.Length > 6 && finish[6] is object?[] blocks ? blocks.Length : 0;
        foreach (var row in party.Cast<object?[]>())
        {
            var baseId = Convert.ToInt64(row[0]);
            foreach (var (mission,count) in new[] { (1L,cleared ? 1 : 0),(2L,row[1] is true ? acts : 0) })
            {
                if (count == 0) continue;
                await ExecuteAsync(connection,transaction,"""
                    insert into user_character_mission_states("userId",character_base_master_id,mission_master_id,current_count)
                    values($1,$2,$3,$4) on conflict("userId",character_base_master_id,mission_master_id)
                    do update set current_count=user_character_mission_states.current_count+excluded.current_count,updated_at=now()
                    """,userId,baseId,mission,(long)count);
            }
        }
    }
}
