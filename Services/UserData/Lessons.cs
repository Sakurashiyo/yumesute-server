using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    public Task<object?[]> CreateLessonPartyAsync(HttpContext context, long characterBaseMasterId) =>
        SaveLessonPartyAsync(context, characterBaseMasterId, null);

    public Task<object?[]> SetLessonPartyAsync(HttpContext context, long characterBaseMasterId, object? payload)
    {
        if (payload is not object?[] { Length: 1 } values || values[0] is not object?[] slots)
            throw new BadHttpRequestException(PartyInvalid, 400);
        return SaveLessonPartyAsync(context, characterBaseMasterId, slots);
    }

    public async Task<object?[]> SetLessonPartyLeaderAsync(HttpContext context, long characterBaseMasterId, int leaderPosition)
    {
        if (leaderPosition is < 0 or > 5) throw new BadHttpRequestException(PartyInvalid, 400);
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException(PartyNotFound, 404);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var party = (await ReadLessonPartiesAsync(connection, userId, transaction)).SingleOrDefault(row => Convert.ToInt64(row[0]) == characterBaseMasterId)
            ?? throw new BadHttpRequestException(PartyNotFound, 404);
        var slots = (object?[][])party[1]!;
        if (leaderPosition > 0 && slots[leaderPosition - 1][1] is null) throw new BadHttpRequestException(PartyMemberInvalid, 400);
        await ExecuteAsync(connection, transaction,
            "update user_live_lesson_party_states set payload=payload || jsonb_build_object('leaderPosition',$3), updated_at=now() where \"userId\"=$1 and lesson_master_id=$2",
            userId, characterBaseMasterId, leaderPosition);
        party[3] = leaderPosition;
        await transaction.CommitAsync();
        return new object?[] { DataObject(39, party) };
    }

    async Task<object?[]> SaveLessonPartyAsync(HttpContext context, long characterBaseMasterId, object?[]? requestedSlots)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException(PartyNotFound, 404);
        if (!CharacterCatalog.Value.Bases.ContainsKey(characterBaseMasterId))
            throw new BadHttpRequestException(PartyMemberInvalid, 400);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var ownedCards = new Dictionary<long, long>();
        await using (var cards = new NpgsqlCommand("select id, character_master_id from user_character_cards where \"userId\" = $1", connection, transaction))
        {
            cards.Parameters.AddWithValue(userId);
            await using var reader = await cards.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ownedCards.Add(reader.GetInt64(0), CharacterCatalog.Value.Cards[reader.GetInt64(1)].BaseId);
        }
        var slots = new long?[5];
        if (requestedSlots is null)
        {
            await using var selected = new NpgsqlCommand("select selected_character_id from user_character_bases where \"userId\" = $1 and character_base_master_id = $2", connection, transaction);
            selected.Parameters.AddWithValue(userId);
            selected.Parameters.AddWithValue(characterBaseMasterId);
            if (await selected.ExecuteScalarAsync() is not long characterId || !ownedCards.TryGetValue(characterId, out var baseId) || baseId != characterBaseMasterId)
                throw new BadHttpRequestException(PartyMemberInvalid, 400);
            slots[0] = characterId;
        }
        else
        {
            if (requestedSlots.Length is < 1 or > 5) throw new BadHttpRequestException(PartyInvalid, 400);
            var positions = new HashSet<long>();
            var characters = new HashSet<long>();
            foreach (var item in requestedSlots)
            {
                if (item is not object?[] { Length: 2 } slot || GetLong(slot, 0) is not long position || position is < 1 or > 5
                    || !positions.Add(position) || GetLong(slot, 1) is not long characterId || characterId < 0)
                    throw new BadHttpRequestException(PartyInvalid, 400);
                if (characterId == 0) continue;
                if (!ownedCards.ContainsKey(characterId) || !characters.Add(characterId))
                    throw new BadHttpRequestException(PartyMemberInvalid, 400);
                slots[position - 1] = characterId;
            }
        }
        var leader = Array.FindIndex(slots, id => id.HasValue && ownedCards[id.Value] == characterBaseMasterId) + 1;
        if (leader == 0) throw new BadHttpRequestException(PartyMemberInvalid, 400);
        var serializedSlots = JsonSerializer.Serialize(Enumerable.Range(0, 5).Select(index => new object?[] { index + 1, slots[index] }));
        await using (var save = new NpgsqlCommand(
            requestedSlots is null
                ? "insert into user_live_lesson_party_states (\"userId\", lesson_master_id, slots, payload) values ($1,$2,$3::jsonb,jsonb_build_object('leaderPosition',$4)) on conflict (\"userId\",lesson_master_id) do nothing"
                : "insert into user_live_lesson_party_states (\"userId\", lesson_master_id, slots, payload) values ($1,$2,$3::jsonb,jsonb_build_object('leaderPosition',$4)) on conflict (\"userId\",lesson_master_id) do update set slots=excluded.slots, payload=user_live_lesson_party_states.payload || excluded.payload, updated_at=now()",
            connection, transaction))
        {
            save.Parameters.AddWithValue(userId);
            save.Parameters.AddWithValue(characterBaseMasterId);
            save.Parameters.AddWithValue(serializedSlots);
            save.Parameters.AddWithValue(leader);
            await save.ExecuteNonQueryAsync();
        }
        var parties = await ReadLessonPartiesAsync(connection, userId, transaction);
        var party = parties.Single(row => Convert.ToInt64(row[0]) == characterBaseMasterId);
        await transaction.CommitAsync();
        return new object?[] { DataObject(39, party) };
    }

    static async Task<object?[][]> ReadLessonPartiesAsync(NpgsqlConnection connection, long userId, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("select lesson_master_id, slots, payload from user_live_lesson_party_states where \"userId\" = $1 order by lesson_master_id", connection, transaction);
        command.Parameters.AddWithValue(userId);
        var parties = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            using var slots = JsonDocument.Parse(reader.GetString(1));
            using var payload = JsonDocument.Parse(reader.GetString(2));
            var details = payload.RootElement;
            parties.Add(new object?[]
            {
                reader.GetInt64(0),
                slots.RootElement.EnumerateArray().Select(slot => new object?[] { slot[0].GetInt32(), slot[1].ValueKind == JsonValueKind.Null ? null : (object?)slot[1].GetInt64() }).ToArray(),
                details.TryGetProperty("bestScore", out var score) ? score.GetInt64() : 0L,
                details.GetProperty("leaderPosition").GetInt32(),
                details.TryGetProperty("rewardReceivedHighScore", out var reward) ? reward.GetInt64() : 0L
            });
        }
        return parties.ToArray();
    }
}
