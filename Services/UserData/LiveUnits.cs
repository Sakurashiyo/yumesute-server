using Npgsql;

sealed partial class UserDataService
{
    async Task<(object?[] Unit, object?[]? AuditionParty)> ReadLiveUnitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long userId, long partyId, long chartId, bool auto, long? auditionSenseNotationId = null)
    {
        var party = (await ReadPartiesAsync(connection, userId)).SingleOrDefault(row => Convert.ToInt64(row[0]) == partyId)
            ?? throw new BadHttpRequestException(PartyNotFound, StatusCodes.Status404NotFound);
        long? senseNotationId;
        await using (var command = new NpgsqlCommand(
            "select sense_notation_master_id from user_party_groups where id=$1 and \"userId\"=$2", connection, transaction))
        {
            command.Parameters.AddWithValue(partyId);
            command.Parameters.AddWithValue(userId);
            var value = await command.ExecuteScalarAsync();
            senseNotationId = value is null or DBNull ? null : Convert.ToInt64(value);
        }
        var slots = (await ReadPartySlotsAsync(connection, userId))
            .Where(row => Convert.ToInt64(row[1]) == partyId && row[3] is not null).ToArray();
        var cards = (await ReadCharactersAsync(connection, userId)).ToDictionary(row => Convert.ToInt64(row[0]));
        ResolvePartyCharacterAliases(userId, slots, cards.Values);
        var selected = slots.Select(slot => cards.TryGetValue(Convert.ToInt64(slot[3]), out var card)
            ? card : throw new BadHttpRequestException(PartyMemberInvalid)).ToArray();
        var statuses = await ReadLessonCharacterStatusesAsync(connection, transaction, userId, selected);
        var members = slots.Select(slot =>
        {
            var card = cards[Convert.ToInt64(slot[3])];
            return new LiveUnitRules.Member(Convert.ToInt32(slot[2]), card,
                (object?[])statuses[Convert.ToInt64(card[0]).ToString(System.Globalization.CultureInfo.InvariantCulture)]!);
        }).ToArray();
        var unit = LiveUnitRules.Build(members, Convert.ToInt32(party[3]), chartId, auto, auditionSenseNotationId ?? senseNotationId);
        var chart = PlayerProgressionRules.Charts[chartId];
        // 隐藏挑战在点击开演时才切到 OLIVIER，客户端需要区分首次挑战和已购买谱面。
        if (chart.Difficulty == 5 && !auto)
        {
            var music = (await ReadLiveMusicStatesAsync(connection, userId))
                .Single(row => Convert.ToInt64(row[2]) == chart.MusicMasterId);
            unit[8] = music[9] is true && Convert.ToInt32(music[8]) == 1;
        }
        var auditionParty = auditionSenseNotationId is null ? null :
            await ReadAuditionPartySnapshotAsync(connection, userId, party, slots, cards, chartId, unit);
        return (unit, auditionParty);
    }
}
