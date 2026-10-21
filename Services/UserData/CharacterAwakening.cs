using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> AwakenCharacterAsync(HttpContext context, long characterId)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        long master;
        int level, phase;
        await using (var read = new NpgsqlCommand("""
            select character_master_id, level, awakening from user_character_cards
            where id=$1 and "userId"=$2 for update
            """, connection, transaction))
        {
            read.Parameters.AddWithValue(characterId);
            read.Parameters.AddWithValue(userId);
            await using var reader = await read.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new BadHttpRequestException(CharacterAwakeningErrors.CharacterMissing, StatusCodes.Status404NotFound);
            master = reader.GetInt64(0);
            level = reader.GetInt32(1);
            phase = reader.GetInt32(2);
        }
        var costs = CharacterAwakeningRules.Required(master, level, phase);
        var present = new List<object?>();
        foreach (var cost in costs)
        {
            var remaining = await ConsumeExperienceItemAsync(connection, transaction, userId, cost.ItemMasterId, cost.Quantity, CharacterAwakeningErrors.ItemInsufficient);
            present.Add(DataObject(27, new object?[] { UserScopedId(userId, cost.ItemMasterId), cost.ItemMasterId, remaining }));
        }
        // 目前客户端只有一次觉醒；重复请求返回已觉醒角色，避免再次扣材料。
        await using (var update = new NpgsqlCommand("""
            update user_character_cards
            set awakening=1,
                display_awakening_status = $3 and (awakening=0 or display_awakening_status)
            where id=$1 and "userId"=$2
            returning id, character_master_id, level, exp, awakening, character_base_id,
                      skill_level, extra_1, extra_2, is_new, display_awakening_status, is_favorite
            """, connection, transaction))
        {
            update.Parameters.AddWithValue(characterId);
            update.Parameters.AddWithValue(userId);
            update.Parameters.AddWithValue(CharacterAwakeningRules.HasAwakeningArt(master));
            await using var reader = await update.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("觉醒更新后角色数据丢失");
            present.Insert(0, DataObject(4, ReadUpdatedCharacter(reader)));
        }
        await transaction.CommitAsync();
        if (phase == 0) context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "角色觉醒 operation=character-awakening userId={UserId} characterId={CharacterId} from={From} to=1 outcome=success", userId, characterId, phase);
        return present.ToArray();
    }

    public async Task<object?[]> SwitchCharacterDisplayAwakeningStatusAsync(HttpContext context, long characterId)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await using var connection = await database.OpenConnectionAsync();
        // 单条更新原子切换，避免并发请求读到同一个旧值。
        await using var command = new NpgsqlCommand("""
            update user_character_cards set display_awakening_status = not display_awakening_status
            where id=$1 and "userId"=$2 and awakening > 0 and character_master_id=any($3)
            returning id, character_master_id, level, exp, awakening, character_base_id,
                      skill_level, extra_1, extra_2, is_new, display_awakening_status, is_favorite
            """, connection);
        command.Parameters.AddWithValue(characterId);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(CharacterAwakeningRules.DisplayMasterIds);
        object?[] card;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
                throw new BadHttpRequestException(CharacterAwakeningErrors.DisplayUnavailable, StatusCodes.Status409Conflict);
            card = ReadUpdatedCharacter(reader);
        }
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "切换角色卡面 operation=character-art-switch userId={UserId} characterId={CharacterId} displayAwakeningStatus={DisplayAwakeningStatus} outcome=success",
            userId, characterId, card[10]);
        return new object?[] { DataObject(4, card) };
    }
}
