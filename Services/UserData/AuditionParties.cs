using System.Globalization;
using Npgsql;

sealed partial class UserDataService
{
    sealed record AuditionEquipment(long Id, long MasterId, int Level, int Phase);

    static async Task<(Dictionary<long, AuditionEquipment> Posters, Dictionary<long, AuditionEquipment> Accessories)>
        ReadAuditionEquipmentAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand("""
            select union_key,(payload->>0)::bigint,(payload->>1)::bigint,(payload->>2)::integer,
              case when union_key=28 then (payload->>3)::integer else 0 end
            from user_raw_union_states where "userId"=$1 and union_key in (26,28)
            """, connection);
        command.Parameters.AddWithValue(userId);
        var posters = new Dictionary<long, AuditionEquipment>();
        var accessories = new Dictionary<long, AuditionEquipment>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = new AuditionEquipment(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3), reader.GetInt32(4));
            (reader.GetInt32(0) == LegacyPosterStorageKey ? posters : accessories).Add(item.Id, item);
        }
        return (posters, accessories);
    }

    async Task<object?[]> ReadAuditionPartySnapshotAsync(NpgsqlConnection connection, long userId,
        object?[] party, object?[][] slots, Dictionary<long, object?[]> cards, long chartId, object?[] unit)
    {
        var bases = (await ReadCharacterBasesAsync(connection, userId)).ToDictionary(row => Convert.ToInt64(row[0]));
        var equipment = await ReadAuditionEquipmentAsync(connection, userId);
        var actors = (Dictionary<int, object?>)unit[0]!;
        AuditionEquipment? Equipped(object? id, Dictionary<long, AuditionEquipment> inventory) => id is null ? null :
            inventory.TryGetValue(Convert.ToInt64(id), out var owned) ? owned :
            throw new BadHttpRequestException(AuditionErrors.PartyUnavailable, StatusCodes.Status409Conflict);
        var members = slots.OrderBy(slot => Convert.ToInt32(slot[2])).Select(slot =>
        {
            var card = cards[Convert.ToInt64(slot[3])];
            var poster = Equipped(slot[4], equipment.Posters);
            var accessory = Equipped(slot[5], equipment.Accessories);
            var actor = (object?[])actors[Convert.ToInt32(slot[2])]!;
            return (object?)new object?[] { slot[2], card[1], card[2], card[4], card[5],
                poster?.MasterId, poster?.Level, poster?.Phase, accessory?.MasterId, accessory?.Level,
                bases[Convert.ToInt64(card[6])][2], card[4], card[5], actor[8], card[10] };
        }).ToArray();
        var profile = await ReadProfileAsync(connection, userId);
        var user = await ReadUserAsync(connection, userId);
        return new object?[] { 0, 0, PlayerProgressionRules.Charts[chartId].Difficulty, members,
            profile[1], 0, null, null, null, Convert.ToInt32(user[1]).ToString(CultureInfo.InvariantCulture), party[3] };
    }

    public async Task<LiveSettlement> CopyAuditionClearPartyAsync(HttpContext context, long auditionId, long partyId)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        _ = AuditionRules.Get(auditionId);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var saved = await ReadAuditionHighScorePartyAsync(connection, userId, auditionId)
            ?? throw new BadHttpRequestException(AuditionErrors.PartyMissing, StatusCodes.Status404NotFound);
        var destination = (await ReadPartiesAsync(connection, userId))
            .SingleOrDefault(row => Convert.ToInt64(row[0]) == partyId)
            ?? throw new BadHttpRequestException(AuditionErrors.PartyMissing, StatusCodes.Status404NotFound);
        var slots = (await ReadPartySlotsAsync(connection, userId)).Where(row => Convert.ToInt64(row[1]) == partyId).ToArray();
        var characters = (await ReadCharactersAsync(connection, userId)).ToDictionary(row => Convert.ToInt64(row[1]));
        var equipment = await ReadAuditionEquipmentAsync(connection, userId);
        var source = ((object?[])saved[3]!).Cast<object?[]>().ToDictionary(row => Convert.ToInt32(row[0]));
        long? OwnedEquipment(object? masterId, Dictionary<long, AuditionEquipment> items) => masterId is null ? null :
            items.Values.Where(item => item.MasterId == Convert.ToInt64(masterId)).OrderByDescending(item => item.Level)
                .ThenBy(item => item.Id).Select(item => (long?)item.Id).FirstOrDefault()
                ?? throw new BadHttpRequestException(AuditionErrors.PartyUnavailable, StatusCodes.Status409Conflict);
        if (source.Keys.Any(position => slots.All(slot => Convert.ToInt32(slot[2]) != position)))
            throw new InvalidDataException("目标编队缺少公演快照中的位置");
        var present = new List<object?>();
        foreach (var slot in slots)
        {
            long? characterId = null, posterId = null, accessoryId = null;
            if (source.TryGetValue(Convert.ToInt32(slot[2]), out var member))
            {
                if (!characters.TryGetValue(Convert.ToInt64(member[1]), out var character))
                    throw new BadHttpRequestException(AuditionErrors.PartyUnavailable, StatusCodes.Status409Conflict);
                characterId = Convert.ToInt64(character[0]);
                posterId = OwnedEquipment(member[5], equipment.Posters);
                accessoryId = OwnedEquipment(member[8], equipment.Accessories);
            }
            await using var update = new NpgsqlCommand("""
                update user_party_slots set character_card_id=$3,poster_id=$4,accessory_id=$5,updated_at=now()
                where id=$1 and "userId"=$2
                """, connection, transaction);
            update.Parameters.AddWithValue(slot[0]!);
            update.Parameters.AddWithValue(userId);
            foreach (var value in new[] { characterId, posterId, accessoryId })
                update.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = (object?)value ?? DBNull.Value });
            await update.ExecuteNonQueryAsync();
            present.Add(DataObject(7, new object?[] { slot[0], partyId, slot[2], characterId, posterId, accessoryId, slot[6] }));
        }
        await ExecuteAsync(connection, transaction, """
            update user_party_groups set party_type=$3,updated_at=now() where id=$1 and "userId"=$2
            """, partyId, userId, Convert.ToInt32(saved[10]));
        destination[3] = saved[10];
        present.Insert(0, DataObject(6, destination));
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "复制公演最高分编队 operation=audition-copy userId={UserId} auditionId={AuditionId} partyId={PartyId}",
            userId, auditionId, partyId);
        return new(new object?[] { true }, present.ToArray());
    }
}
