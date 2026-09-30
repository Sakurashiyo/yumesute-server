using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> EnhanceCharacterSenseAsync(HttpContext context, long characterId, int target, int priority)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        // Secondary Sense 尚无持久化字段，不能把它误写进 Primary Sense。
        if (priority != 1 || target is < 1 or > 5) throw new BadHttpRequestException(CharacterSenseErrors.InvalidRequest);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        long masterId;
        int current;
        await using (var read = new NpgsqlCommand("""
            select character_master_id, skill_level from user_character_cards
            where id = $1 and "userId" = $2 for update
            """, connection, transaction))
        {
            read.Parameters.AddWithValue(characterId);
            read.Parameters.AddWithValue(userId);
            await using var reader = await read.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new BadHttpRequestException(CharacterSenseErrors.CharacterMissing, StatusCodes.Status404NotFound);
            masterId = reader.GetInt64(0);
            current = reader.GetInt32(1);
        }
        // 目标等级而非增量：重试或并发重复点击不会重复扣除。
        var costs = CharacterSenseRules.Costs(masterId, current, target);
        var present = new List<object?>();
        foreach (var cost in costs)
        {
            var remaining = await ConsumeExperienceItemAsync(connection, transaction, userId, cost.ItemMasterId, cost.Quantity, CharacterSenseErrors.ItemInsufficient);
            present.Add(DataObject(27, new object?[] { UserScopedId(userId, cost.ItemMasterId), cost.ItemMasterId, remaining }));
        }
        await using (var update = new NpgsqlCommand("""
            update user_character_cards set skill_level = $3 where id = $1 and "userId" = $2
            returning id, character_master_id, level, exp, awakening, character_base_id,
                      skill_level, extra_1, extra_2, is_new
            """, connection, transaction))
        {
            update.Parameters.AddWithValue(characterId);
            update.Parameters.AddWithValue(userId);
            update.Parameters.AddWithValue(target);
            await using var reader = await update.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Sense 更新后角色数据丢失");
            present.Insert(0, DataObject(4, ReadUpdatedCharacter(reader)));
        }
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "Sense 升级 userId={UserId} characterId={CharacterId} from={From} to={To} itemTypes={ItemTypes}", userId, characterId, current, target, costs.Length);
        return present.ToArray();
    }
}
