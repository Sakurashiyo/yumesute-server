using Npgsql;

sealed partial class UserDataService
{
    const string PartyInvalid = "PARTY_INVALID_PAYLOAD";
    const string PartyNotFound = "PARTY_NOT_FOUND";
    const string PartyMemberInvalid = "PARTY_MEMBER_INVALID";

    public async Task<object?[]> EditPartyAsync(HttpContext context, long partyId, object? payload)
    {
        if (payload is not object?[] { Length: 6 } values || values[0] is not object?[] { Length: 5 } slots)
            throw new BadHttpRequestException(PartyInvalid, 400);
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException(PartyNotFound, 404);
        await EnsureDefaultUserDataAsync(userId);
        var synchronized = await ReadUserDataAsync(userId);
        object?[][] Objects(int key) => synchronized.OfType<object?[]>()
            .Where(item => item[0] is int type && type == key).Select(item => (object?[])item[1]!).ToArray();
        var availableCards = Objects(4).Select(card => Convert.ToInt64(card[0])).ToHashSet();
        var accessories = Objects(26).Select(item => Convert.ToInt64(item[0])).ToHashSet();
        var posters = Objects(28).Select(item => Convert.ToInt64(item[0])).ToHashSet();
        var leader = GetNullableLong(values, 5);
        if (leader is < 1 or > 5) throw new BadHttpRequestException(PartyInvalid, 400);
        var members = slots.Select(slot =>
        {
            if (slot is not object?[] { Length: 5 } member) throw new BadHttpRequestException(PartyInvalid, 400);
            var slotId = GetLong(member, 0) ?? throw new BadHttpRequestException(PartyInvalid, 400);
            var character = GetNullableLong(member, 1);
            var accessory = GetNullableLong(member, 2);
            var poster = GetNullableLong(member, 3);
            var flags = GetLong(member, 4) ?? throw new BadHttpRequestException(PartyInvalid, 400);
            if (flags is < 0 or > 31 || character is < 0 || accessory is < 0 || poster is < 0)
                throw new BadHttpRequestException(PartyInvalid, 400);
            return (SlotId: slotId, Character: character is 0 ? null : character,
                Accessory: accessory is 0 ? null : accessory, Poster: poster is 0 ? null : poster, Flags: (int)flags);
        }).ToArray();
        if (members.Select(member => member.SlotId).Distinct().Count() != 5 ||
            members.Where(member => member.Character.HasValue).Select(member => member.Character).Distinct().Count() != members.Count(member => member.Character.HasValue))
            throw new BadHttpRequestException(PartyInvalid, 400);

        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // 与存档管理使用相同玩家锁，避免编队引用正在移除的卡片。
        await using (var userLock = new NpgsqlCommand("select \"userId\" from user_game_states where \"userId\" = $1 for update", connection, transaction))
        { userLock.Parameters.AddWithValue(userId); await userLock.ExecuteScalarAsync(); }
        var currentCards = new HashSet<long>();
        await using (var findCards = new NpgsqlCommand("select id from user_character_cards where \"userId\"=$1 for key share",connection,transaction))
        {
            findCards.Parameters.AddWithValue(userId);
            await using var reader = await findCards.ExecuteReaderAsync();
            while(await reader.ReadAsync()) currentCards.Add(reader.GetInt64(0));
        }
        // 仍兼容初始化阶段的虚拟默认卡片，但不允许已删除的实际卡片进入队伍。
        var virtualCards = Enumerable.Range(0,BootstrapCharacterMasterIds.Length)
            .Select(index => UserScopedId(userId,1001 + index)).Where(availableCards.Contains).ToHashSet();
        object?[] party;
        await using (var find = new NpgsqlCommand("select id,display_index,name,party_type from user_party_groups where id = $1 and \"userId\" = $2 for update", connection, transaction))
        {
            find.Parameters.AddWithValue(partyId); find.Parameters.AddWithValue(userId);
            await using var reader = await find.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new BadHttpRequestException(PartyNotFound, 404);
            party = new object?[] { reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), leader.HasValue ? (int)leader.Value : reader.GetInt32(3) };
        }
        var previous = new Dictionary<long, object?[]>();
        await using (var find = new NpgsqlCommand("select id,party_id,slot_number,character_card_id,accessory_id,poster_id,position from user_party_slots where party_id = $1 and \"userId\" = $2 order by slot_number", connection, transaction))
        {
            find.Parameters.AddWithValue(partyId); find.Parameters.AddWithValue(userId);
            await using var reader = await find.ExecuteReaderAsync();
            while (await reader.ReadAsync()) previous.Add(reader.GetInt64(0), new object?[] { reader.GetInt64(0), partyId, reader.GetInt32(2), GetNullableInt64(reader,3), GetNullableInt64(reader,4), GetNullableInt64(reader,5), reader.GetInt32(6) });
        }
        var changes = new List<object?> { DataObject(6, party) };
        foreach (var member in members)
        {
            if (!previous.TryGetValue(member.SlotId, out var old) ||
                (member.Character.HasValue && (!availableCards.Contains(member.Character.Value) ||
                    (!currentCards.Contains(member.Character.Value) && !virtualCards.Contains(member.Character.Value)))) ||
                (member.Accessory.HasValue && !accessories.Contains(member.Accessory.Value) && !Equals(member.Accessory, old[4])) ||
                (member.Poster.HasValue && !posters.Contains(member.Poster.Value) && !Equals(member.Poster, old[5])))
                throw new BadHttpRequestException(PartyMemberInvalid, 400);
            await using var update = new NpgsqlCommand(
                "update user_party_slots set character_card_id=$3,accessory_id=$4,poster_id=$5,position=$6,updated_at=now() where id=$1 and \"userId\"=$2", connection, transaction);
            update.Parameters.AddWithValue(member.SlotId); update.Parameters.AddWithValue(userId);
            update.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = (object?)member.Character ?? DBNull.Value });
            update.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = (object?)member.Accessory ?? DBNull.Value });
            update.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = (object?)member.Poster ?? DBNull.Value });
            update.Parameters.AddWithValue(member.Flags); await update.ExecuteNonQueryAsync();
            changes.Add(DataObject(7,new object?[] { member.SlotId,partyId,old[2],member.Character,member.Accessory,member.Poster,member.Flags }));
        }
        // 现有 party_type 列承载客户端 Party.Key(3)，即队长位置，沿用原存档映射。
        await using (var update = new NpgsqlCommand("update user_party_groups set party_type=$3,updated_at=now() where id=$1 and \"userId\"=$2",connection,transaction))
        { update.Parameters.AddWithValue(partyId); update.Parameters.AddWithValue(userId); update.Parameters.AddWithValue((int)party[3]!); await update.ExecuteNonQueryAsync(); }
        await transaction.CommitAsync();
        return changes.ToArray();
    }
}
