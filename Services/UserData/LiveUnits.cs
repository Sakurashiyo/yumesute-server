using Npgsql;

sealed partial class UserDataService
{
    async Task<object?[]> ReadLiveUnitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long userId, long partyId, long chartId, bool auto)
    {
        var party = (await ReadPartiesAsync(connection, userId)).SingleOrDefault(row => Convert.ToInt64(row[0]) == partyId)
            ?? throw new BadHttpRequestException(PartyNotFound, StatusCodes.Status404NotFound);
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
        return LiveUnitRules.Build(members, Convert.ToInt32(party[3]), chartId, auto);
    }
}
