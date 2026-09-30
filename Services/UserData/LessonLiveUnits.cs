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
        var statuses = session.Party.Length >= 3
            ? (Dictionary<string, object?>)session.Party[2]!
            : await ReadLessonCharacterStatusesAsync(connection, transaction, userId, cards.Values);
        if (session.Party.Length < 3)
        {
            // 为修复前尚未完成的场次补存战力快照，后续重试保持一致。
            await ExecuteAsync(connection, transaction, "update user_lesson_sessions set party_snapshot=$2 where \"userId\"=$1",
                userId, MsgPack.Encode(new object?[] { party, session.Party[1], statuses }));
        }
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
        // 角色与战力使用开始时的快照，重试开始不会受之后强化或高分变化影响。
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
            actor[4] = card[4];
            actor[7] = statuses[cardId.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            actor[8] = statuses[cardId.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            actor[5] = position;
            actor[6] = card[7];
            actors.Add(position, actor);
            var sense = (object?[])senseTemplate.Clone();
            // 技能 ID 仅用于本场引用，按唯一槽位生成，避免大卡片 ID 的乘法溢出。
            var senseId = (long)position;
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
        unit[5] = checked(actors.Values.Cast<object?[]>().Sum(actor => Convert.ToInt32(((object?[])actor[8]!)[3])));
        await transaction.CommitAsync();
        return unit;
    }
    async Task<Dictionary<string, object?>> ReadLessonCharacterStatusesAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, long userId, IEnumerable<object?[]> cards)
    {
        var bases = (await ReadCharacterBasesAsync(connection, userId)).ToDictionary(row => Convert.ToInt64(row[1]));
        var scores = (await ReadLessonPartiesAsync(connection, userId, transaction)).ToDictionary(
            row => Convert.ToInt64(row[0]), row => Convert.ToInt64(row[2]));
        // 尚未为该角色创建稽古编队时没有高分记录，按客户端的初始高分 0 计算。
        return cards.ToDictionary(card => Convert.ToInt64(card[0]).ToString(System.Globalization.CultureInfo.InvariantCulture), card =>
        {
            var baseId = CharacterCatalog.Value.Cards[Convert.ToInt64(card[1])].BaseId;
            return (object?)CharacterStatusRules.Calculate(card, Convert.ToInt32(bases[baseId][2]), scores.GetValueOrDefault(baseId));
        });
    }
}
