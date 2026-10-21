using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> CopyPartyAsync(HttpContext context, int srcOrder, int destOrder)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        if (srcOrder < 0 || destOrder < 0) throw new BadHttpRequestException(PartyInvalid, 400);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // 与编辑编队、存档管理共用玩家锁，复制读取和目标写入保持一致。
        await LockLessonUserAsync(connection, transaction, userId);
        var parties = await ReadPartiesAsync(connection, userId);
        var source = parties.SingleOrDefault(row => Convert.ToInt32(row[1]) == srcOrder)
            ?? throw new BadHttpRequestException(PartyNotFound, 404);
        var destination = parties.SingleOrDefault(row => Convert.ToInt32(row[1]) == destOrder)
            ?? throw new BadHttpRequestException(PartyNotFound, 404);
        var sourceId = Convert.ToInt64(source[0]);
        var destinationId = Convert.ToInt64(destination[0]);
        var allSlots = await ReadPartySlotsAsync(connection, userId);
        var sourceSlots = allSlots.Where(row => Convert.ToInt64(row[1]) == sourceId).ToArray();
        var targetSlots = allSlots.Where(row => Convert.ToInt64(row[1]) == destinationId).ToArray();
        if (sourceSlots.Length != 5 || targetSlots.Length != 5
            || !sourceSlots.Select(row => Convert.ToInt32(row[2])).Order().SequenceEqual(Enumerable.Range(1, 5))
            || !targetSlots.Select(row => Convert.ToInt32(row[2])).Order().SequenceEqual(Enumerable.Range(1, 5)))
            throw new InvalidOperationException("复制编队的五个位置不完整");
        var cards = await ReadCharactersAsync(connection, userId);
        ResolvePartyCharacterAliases(userId, sourceSlots, cards);
        var cardIds = cards.Select(row => Convert.ToInt64(row[0])).ToHashSet();
        var equipment = await ReadAuditionEquipmentAsync(connection, userId);
        var members = sourceSlots.ToDictionary(row => Convert.ToInt32(row[2]));
        foreach (var slot in sourceSlots)
        {
            if (slot[3] is not null && !cardIds.Contains(Convert.ToInt64(slot[3]))
                || slot[4] is not null && !equipment.Posters.ContainsKey(Convert.ToInt64(slot[4]))
                || slot[5] is not null && !equipment.Accessories.ContainsKey(Convert.ToInt64(slot[5])))
                throw new BadHttpRequestException(PartyMemberInvalid, 400);
        }

        // 编队身份、顺序与名称属于目标；复制养成引用、队长和 Sense 时轴。
        await ExecuteAsync(connection, transaction, """
            update user_party_groups destination
            set party_type=source.party_type,sense_notation_master_id=source.sense_notation_master_id,updated_at=now()
            from user_party_groups source
            where destination."userId"=$1 and destination.id=$2
              and source."userId"=$1 and source.id=$3
            """, userId, destinationId, sourceId);
        destination[3] = source[3];
        var present = new List<object?> { DataObject(6, destination) };
        foreach (var target in targetSlots)
        {
            var member = members[Convert.ToInt32(target[2])];
            await using var update = new NpgsqlCommand("""
                update user_party_slots
                set character_card_id=$3,poster_id=$4,accessory_id=$5,position=$6,updated_at=now()
                where "userId"=$1 and id=$2 and party_id=$7
                """, connection, transaction);
            update.Parameters.AddWithValue(userId);
            update.Parameters.AddWithValue(target[0]!);
            foreach (var value in member.Skip(3).Take(3))
                update.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = value ?? DBNull.Value });
            update.Parameters.AddWithValue(Convert.ToInt32(member[6]));
            update.Parameters.AddWithValue(destinationId);
            if (await update.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("复制时目标编队位置丢失");
            present.Add(DataObject(7, new object?[] { target[0], destinationId, target[2], member[3], member[4], member[5], member[6] }));
        }
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "复制编队 operation=party-copy userId={UserId} srcOrder={SrcOrder} destOrder={DestOrder} sourceId={SourceId} destinationId={DestinationId} outcome=success",
            userId, srcOrder, destOrder, sourceId, destinationId);
        return present.ToArray();
    }
}
