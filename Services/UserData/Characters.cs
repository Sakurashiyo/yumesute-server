using Npgsql;

sealed partial class UserDataService
{
    static readonly Lazy<AdminSaveCatalog> CharacterCatalog = new(() => new AdminSaveCatalog());

    public async Task<object?[]> SetCostumeAsync(HttpContext context, long characterBaseMasterId, long costumeMasterId)
    {
        var userId = await GetCurrentUserIdAsync(context)
            ?? throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        if (!CharacterCatalog.Value.CanWearCostume(costumeMasterId, characterBaseMasterId))
        {
            throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        }

        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            update user_character_bases as character_base
            set costume_master_id = $3
            where character_base."userId" = $1
              and character_base.character_base_master_id = $2
              and exists (
                select 1 from user_character_costumes
                where "userId" = $1 and costume_master_id = $3
              )
            returning character_base.id
            """, connection);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(characterBaseMasterId);
        command.Parameters.AddWithValue(costumeMasterId);
        if (await command.ExecuteScalarAsync() is not long)
        {
            throw new BadHttpRequestException(CharacterProgressionErrors.CharacterMissing, StatusCodes.Status404NotFound);
        }

        var characterBase = (await ReadCharacterBasesAsync(connection, userId))
            .Single(row => Convert.ToInt64(row[1]) == characterBaseMasterId);
        return new object?[] { DataObject(5, characterBase) };
    }

    public async Task<object?[]> SetPortalCharacterAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context)
            ?? throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        if (payload is not object?[] { Length: >= 3 } values
            || GetLong(values, 0) is not long characterBaseId
            || GetLong(values, 1) is not long characterMasterId
            || GetBool(values, 2) is not bool awakening)
        {
            throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        }

        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            update user_character_bases as character_base
            set selected_character_id = selected.id, portal_display_awakening_status = $4
            from user_character_cards selected
            where character_base."userId" = $1
              and character_base.id = $2
              and selected."userId" = $1
              and selected.character_master_id = $3
              and selected.character_base_id = character_base.id
            returning character_base.character_base_master_id
            """, connection);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(characterBaseId);
        command.Parameters.AddWithValue(characterMasterId);
        command.Parameters.AddWithValue(awakening);
        if (await command.ExecuteScalarAsync() is not long characterBaseMasterId)
        {
            throw new BadHttpRequestException(CharacterProgressionErrors.CharacterMissing, StatusCodes.Status404NotFound);
        }

        var characterBase = (await ReadCharacterBasesAsync(connection, userId))
            .Single(row => Convert.ToInt64(row[1]) == characterBaseMasterId);
        return new object?[] { DataObject(5, characterBase) };
    }

    public async Task<object?[]> AddCharacterExperienceAsync(HttpContext context, long characterId, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context)
            ?? throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        var consumedItems = ReadExperienceItems(payload);

        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var character = await ReadCharacterForUpdateAsync(connection, transaction, userId, characterId);
        decimal bonus;
        await using (var readBonus = new NpgsqlCommand("select experience_bonus from user_growth_bonuses where \"userId\"=$1 for update", connection, transaction))
        {
            readBonus.Parameters.AddWithValue(userId);
            bonus = (decimal)(await readBonus.ExecuteScalarAsync() ?? throw new InvalidOperationException("账号经验加成缺失"));
        }
        var growth = CharacterProgressionRules.UseItems(consumedItems.Select(i => (i.ItemMasterId, i.Quantity)), bonus,
            CharacterProgressionRules.Remaining(character.MasterId, character.Level, character.Experience));
        var updatedItems = new List<ExperienceItemRequest>();
        foreach (var (itemMasterId, quantity, _) in consumedItems.OrderBy(i => i.ItemMasterId))
        {
            var remaining = await ConsumeExperienceItemAsync(connection, transaction, userId, itemMasterId, quantity);
            updatedItems.Add(new ExperienceItemRequest(itemMasterId, quantity, remaining));
        }

        var next = CharacterProgressionRules.ApplyExperience(character.Level, character.Experience, growth.Gain, character.MasterId);
        var updatedCharacter = await UpdateCharacterExperienceAsync(connection, transaction, userId, characterId, next);
        await using (var updateBonus = new NpgsqlCommand("update user_growth_bonuses set experience_bonus=$2 where \"userId\"=$1", connection, transaction))
        {
            updateBonus.Parameters.AddWithValue(userId);
            updateBonus.Parameters.AddWithValue(growth.Bonus);
            await updateBonus.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();

        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "角色经验更新 operation=character-experience userId={UserId} characterId={CharacterId} from={From} to={To} gain={Gain} bonus={Bonus} outcome=success",
            userId, characterId, character.Level, next.Level, growth.Gain, growth.Bonus);
        var present = new List<object?> { DataObject(4, updatedCharacter), DataObject(67, new object?[] { userId, (float)growth.Bonus, 0f }) };
        present.AddRange(updatedItems.Select(item => DataObject(27, new object?[] { UserScopedId(userId, item.ItemMasterId), item.ItemMasterId, item.QuantityRemaining })));
        return present.ToArray();
    }

    static IReadOnlyList<ExperienceItemRequest> ReadExperienceItems(object? payload)
    {
        if (payload is not object?[] rows || rows.Length == 0)
        {
            throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        }

        var items = rows.Select(row =>
        {
            if (row is not object?[] values || values.Length != 2 || GetLong(values, 0) is not long itemMasterId || GetLong(values, 1) is not long quantityLong || quantityLong is <= 0 or > int.MaxValue || !CharacterProgressionRules.TryGetItemExperience(itemMasterId, out _))
            {
                throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
            }

            return new ExperienceItemRequest(itemMasterId, checked((int)quantityLong), 0);
        }).GroupBy(item => item.ItemMasterId).Select(group => new ExperienceItemRequest(group.Key, checked(group.Sum(item => item.Quantity)), 0)).ToArray();
        return items;
    }

    static async Task<CharacterExperienceState> ReadCharacterForUpdateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, long characterId)
    {
        await using var command = new NpgsqlCommand(
            """
            select level, exp, character_master_id
            from user_character_cards
            where id = $1 and "userId" = $2
            for update
            """, connection, transaction);
        command.Parameters.AddWithValue(characterId);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new BadHttpRequestException(CharacterProgressionErrors.CharacterMissing, StatusCodes.Status404NotFound);
        return new CharacterExperienceState(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2));
    }

    static async Task<int> ConsumeExperienceItemAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, long itemMasterId, int quantity, string insufficientError = CharacterProgressionErrors.ItemInsufficient)
    {
        await using var command = new NpgsqlCommand(
            """
            update user_item_possessions
            set quantity = quantity - $3,
                updated_at = now()
            where "userId" = $1 and item_master_id = $2 and quantity >= $3
            returning quantity
            """, connection, transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(itemMasterId);
        command.Parameters.AddWithValue(quantity);
        var value = await command.ExecuteScalarAsync();
        if (value is not int remaining) throw new BadHttpRequestException(insufficientError, StatusCodes.Status409Conflict);
        return remaining;
    }

    static async Task<object?[]> UpdateCharacterExperienceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, long characterId, (int Level, int Experience) next)
    {
        await using var command = new NpgsqlCommand(
            """
            update user_character_cards
            set level = $3,
                exp = $4
            where id = $1 and "userId" = $2
            returning id, character_master_id, level, exp, awakening, character_base_id,
                      skill_level, extra_1, extra_2, is_new
            """, connection, transaction);
        command.Parameters.AddWithValue(characterId);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(next.Level);
        command.Parameters.AddWithValue(next.Experience);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("角色经验更新后无法读取角色数据");
        return ReadUpdatedCharacter(reader);
    }

    static object?[] ReadUpdatedCharacter(NpgsqlDataReader reader) => new object?[]
        {
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3), 0,
            reader.GetInt32(4), reader.GetInt64(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8),
            false, null, 0, 1, reader.GetBoolean(9)
        };
    readonly record struct ExperienceItemRequest(long ItemMasterId, int Quantity, int QuantityRemaining);
    readonly record struct CharacterExperienceState(int Level, int Experience, long MasterId);
}
