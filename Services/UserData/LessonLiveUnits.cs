using Npgsql;

sealed partial class UserDataService
{
    async Task<object?[]> ReadLessonLiveUnitAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException(PartyNotFound, 404);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var session = await ReadLessonSessionAsync(connection, transaction, userId)
            ?? throw new BadHttpRequestException(LessonErrors.SessionMissing, 409);
        if (session.Completion is not null) throw new BadHttpRequestException(LessonErrors.SessionConflict, 409);
        var party = (object?[])session.Party[0]!;
        var cards = ((object?[])session.Party[1]!).Cast<object?[]>().ToDictionary(card => Convert.ToInt64(card[0]));
        var unit = GameResults.LiveStartLessonResult();
        var actorTemplate = (object?[])((Dictionary<int, object?>)unit[0]!)[1]!;
        var senseTemplate = (object?[])((object?[])unit[2]!)[0]!;
        var timings = (Dictionary<int, object?>)unit[1]!;
        var activeTimingTemplate = (object?[])timings[17]!;
        foreach (var key in timings.Keys.ToArray())
        {
            var timing = (object?[])((object?[])timings[key]!).Clone();
            timing[5] = Array.Empty<object?>();
            timing[6] = Array.Empty<object?>();
            timings[key] = timing;
        }
        var actors = new Dictionary<int, object?>();
        var senses = new List<object?>();
        // 角色及引用使用开始时的编队快照；战力和技能参数仍沿用现有演出模板。
        foreach (var slot in ((object?[])party[1]!).Cast<object?[]>())
        {
            if (slot[1] is null) continue;
            var position = Convert.ToInt32(slot[0]);
            var cardId = Convert.ToInt64(slot[1]);
            var card = cards[cardId];
            var masterId = Convert.ToInt64(card[1]);
            var actor = (object?[])actorTemplate.Clone();
            actor[0] = cardId;
            actor[1] = CharacterCatalog.Value.Cards[masterId].BaseId;
            actor[2] = masterId;
            actor[3] = card[5];
            actor[5] = position;
            actor[6] = card[7];
            actors.Add(position, actor);
            var sense = (object?[])senseTemplate.Clone();
            var senseId = checked(cardId * 100);
            sense[0] = senseId;
            sense[1] = cardId;
            sense[11] = masterId;
            sense[12] = cardId;
            senses.Add(sense);
            var timing = (object?[])activeTimingTemplate.Clone();
            timing[5] = new object?[] { senseId };
            timings[position * 17] = timing;
        }
        unit[0] = actors;
        unit[2] = senses.ToArray();
        unit[5] = checked(Convert.ToInt32(unit[5]) * actors.Count);
        await transaction.CommitAsync();
        return unit;
    }
}
