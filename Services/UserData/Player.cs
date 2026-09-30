using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    const string ProfileFavoriteCharacterInvalid = "PROFILE_FAVORITE_CHARACTER_INVALID";
    public async Task<object?[]> GetCurrentUserProfileDetailAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null) return BuildUserProfileDetail(null, "LocalPlayer", DefaultCharacterMasterId, 1, false, DefaultIconFrameMasterId, DefaultNameColorMasterId, DefaultNameBaseColorMasterId, null);

        await EnsureDefaultUserDataAsync(userId.Value);
        return await ReadUserProfileDetailAsync(userId.Value);
    }

    public async Task<object?[]> EditProfileAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null || payload is not object?[] rawValues) return Array.Empty<object?>();
        var values = rawValues is [object?[] nestedValues] ? nestedValues : rawValues;

        await EnsureDefaultUserDataAsync(userId.Value);

        var name = GetString(values, 0);
        var introduction = GetString(values, 1);
        var favoriteCharacterId = GetLong(values, 2);
        var nameplateId = GetNullableLong(values, 3);
        var nameColorId = GetLong(values, 4);
        var isPublic = GetBool(values, 8);
        var displayAwakening = GetBool(values, 9);
        var mainCharacterMasterId = GetLong(values, 10);
        var nameBaseColorMasterId = GetLong(values, 11);
        var iconFrameMasterId = GetLong(values, 12);
        var homeSkinMasterId = GetLong(values, 13);

        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        if (favoriteCharacterId is not null && favoriteCharacterId != 0)
        {
            await using var favoriteCommand = new NpgsqlCommand(
                "select id from user_character_cards where id = $1 and \"userId\" = $2 for key share", connection, transaction);
            favoriteCommand.Parameters.AddWithValue(favoriteCharacterId.Value);
            favoriteCommand.Parameters.AddWithValue(userId.Value);
            if (await favoriteCommand.ExecuteScalarAsync() is null)
                throw new BadHttpRequestException(ProfileFavoriteCharacterInvalid, StatusCodes.Status400BadRequest);
        }
        await using var command = new NpgsqlCommand(
            """
            update user_profiles
            set display_name = coalesce($2, display_name),
                comment = coalesce($3, comment),
                favorite_character_id = case when $4 is null then favorite_character_id else nullif($4, 0) end,
                nameplate_master_id = $5,
                name_color_master_id = case when $6 > 0 then $6 else name_color_master_id end,
                is_public = coalesce($7, is_public),
                is_home_character_illust = $8,
                home_character_master_id = case when $9 > 0 then $9 else home_character_master_id end,
                name_base_color_master_id = case when $10 > 0 then $10 else name_base_color_master_id end,
                icon_frame_master_id = case when $11 > 0 then $11 else icon_frame_master_id end,
                home_skin_master_id = case when $12 > 0 then $12 else home_skin_master_id end,
                updated_at = now()
            where "userId" = $1
            """,
            connection, transaction);
        command.Parameters.AddWithValue(userId.Value);
        command.Parameters.AddWithValue((object?)name ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)introduction ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)favoriteCharacterId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)nameplateId ?? DBNull.Value);
        command.Parameters.AddWithValue(nameColorId ?? 0L);
        command.Parameters.AddWithValue((object?)isPublic ?? DBNull.Value);
        command.Parameters.AddWithValue(displayAwakening ?? false);
        command.Parameters.AddWithValue(mainCharacterMasterId ?? 0L);
        command.Parameters.AddWithValue(nameBaseColorMasterId ?? 0L);
        command.Parameters.AddWithValue(iconFrameMasterId ?? 0L);
        command.Parameters.AddWithValue(homeSkinMasterId ?? 0L);
        await command.ExecuteNonQueryAsync();
        // 客户端的 0 表示取消选择；数据库使用 NULL 表示无角色，不能写入外键 0。
        var profile = await ReadProfileAsync(connection, userId.Value);
        await transaction.CommitAsync();

        return new object?[] { DataObject(1, profile) };
    }

    public async Task<string> GetCurrentUserPushTokenAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null) return "100000";

        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "select numeric_id from user_accounts where id = $1",
            connection);
        command.Parameters.AddWithValue(userId.Value);

        var value = await command.ExecuteScalarAsync();
        return value is int numericId ? numericId.ToString("D6") : FormatNumericId(userId.Value);
    }

    public async Task UpdateHomeLastTransitionTimeAsync(HttpContext context)
    {
        await UpdateUserGameStateTimestampAsync(context, "home_last_transition_at");
    }

    public async Task UpdateSplashLastDisplayTimeAsync(HttpContext context)
    {
        await UpdateUserGameStateTimestampAsync(context, "splash_last_displayed_at");
    }

    public async Task<object?[]> UpdateBirthDateAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        var birthDate = ExtractFirstDateTime(payload);
        if (userId is null || birthDate is null) return Array.Empty<object?>();

        await EnsureDefaultUserDataAsync(userId.Value);
        var date = DateTime.SpecifyKind(birthDate.Value.Date, DateTimeKind.Utc);
        var seconds = new DateTimeOffset(date).ToUnixTimeSeconds();

        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            update user_profiles
            set birthday = $2,
                updated_at = now()
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId.Value);
        command.Parameters.AddWithValue((double)seconds);
        await command.ExecuteNonQueryAsync();

        var preference = await ReadUserPreferenceAsync(connection, userId.Value);
        var selectedPartyId = preference.ElementAtOrDefault(1) ?? UserScopedId(userId.Value, 4001);
        return new object?[] { DataObject(2, new object?[] { userId.Value, selectedPartyId, date }) };
    }

    public async Task<object?[]> UpdateNotificationReadTimeAsync(HttpContext context, object? payload = null)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return Array.Empty<object?>();

        var tabCategory = ExtractFirstInt(payload);
        var readAt = ExtractFirstDateTime(payload) ?? DateTime.UtcNow;
        if (tabCategory is not (>= 1 and <= 3))
        {
            tabCategory = 1;
        }

        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var command = new NpgsqlCommand(
            """
            insert into user_notification_states (
              "userId",
              notification_read_at,
              notice_read_at,
              present_read_at,
              updated_at
            )
            values (
              $1,
              case when $3 = 1 then $2 else null end,
              case when $3 = 2 then $2 else null end,
              case when $3 = 3 then $2 else null end,
              now()
            )
            on conflict ("userId")
            do update set
              notification_read_at = case
                when $3 = 1 then excluded.notification_read_at
                else user_notification_states.notification_read_at
              end,
              notice_read_at = case
                when $3 = 2 then excluded.notice_read_at
                else user_notification_states.notice_read_at
              end,
              present_read_at = case
                when $3 = 3 then excluded.present_read_at
                else user_notification_states.present_read_at
              end,
              updated_at = now()
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(userId.Value);
            command.Parameters.AddWithValue(readAt);
            command.Parameters.AddWithValue(tabCategory.Value);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            tabCategory is >= 1 and <= 3
                ? """
                  insert into user_notification_reads ("userId", notification_id, read_at)
                  select $1, id, $2
                  from system_notifications
                  where notification_tab_category = $3
                    and posting_at <= $2
                  on conflict ("userId", notification_id)
                  do update set read_at = excluded.read_at
                  """
                : """
                  insert into user_notification_reads ("userId", notification_id, read_at)
                  select $1, id, $2
                  from system_notifications
                  where posting_at <= $2
                  on conflict ("userId", notification_id)
                  do update set read_at = excluded.read_at
                  """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(userId.Value);
            command.Parameters.AddWithValue(readAt);
            if (tabCategory is >= 1 and <= 3) command.Parameters.AddWithValue(tabCategory.Value);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return new object?[]
        {
            DataObject(94, await ReadNotificationAsync(connection, userId.Value))
        };
    }

    public async Task<object?[]> UpdateGameHintReadAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return Array.Empty<object?>();

        var categories = ExtractLongs(payload)
            .Select(value => (int)value)
            .DefaultIfEmpty(0)
            .Distinct()
            .ToArray();

        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(
            """
            update user_game_states
            set has_unread_game_hint = false,
                updated_at = now()
            where "userId" = $1
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(userId.Value);
            await command.ExecuteNonQueryAsync();
        }

        var presentData = new List<object?>();
        foreach (var category in categories)
        {
            var gameHintReadId = UserScopedId(userId.Value, 6600 + category);
            await using var command = new NpgsqlCommand(
                """
                insert into user_game_hint_reads (id, "userId", category, is_read, read_at)
                values ($1, $2, $3, true, now())
                on conflict ("userId", category)
                do update set is_read = true,
                              read_at = now()
                returning id
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue(gameHintReadId);
            command.Parameters.AddWithValue(userId.Value);
            command.Parameters.AddWithValue(category);
            var id = await command.ExecuteScalarAsync();
            if (id is long)
            {
                presentData.Add(DataObject(66, new object?[] { gameHintReadId, category, true }));
            }
        }

        await transaction.CommitAsync();
        return presentData.ToArray();
    }

    public async Task<object?[]> GetStoryEventCampInfoAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        var publicId = userId is null ? "0000000000" : await ReadPublicIdAsync(userId.Value);
        var rawRanking = new object?[] { 0, 0, publicId, 0 };
        return new object?[] { rawRanking, 0L, 0L };
    }

    public async Task<object?[]> UpdateTutorialAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return Array.Empty<object?>();

        var tutorialStatus = ExtractFirstInt(payload) ?? 99;
        await using var connection = await database.OpenConnectionAsync();
        await using (var command = new NpgsqlCommand(
            """
            update user_game_states
            set tutorial_status = $2,
                is_tutorial_finished = $3,
                updated_at = now()
            where "userId" = $1
            """,
            connection))
        {
            command.Parameters.AddWithValue(userId.Value);
            command.Parameters.AddWithValue(tutorialStatus);
            command.Parameters.AddWithValue(tutorialStatus == 99);
            await command.ExecuteNonQueryAsync();
        }

        var presentData = new List<object?>();
        if (tutorialStatus == 99)
        {
            presentData.Add(DataObject(122, new object?[] { UserScopedId(userId.Value, 12201), 1, new DateTimeOffset(2026, 7, 21, 13, 0, 0, TimeSpan.FromHours(8)).UtcDateTime }));
            presentData.Add(DataObject(150, new object?[] { UserScopedId(userId.Value, 15001), 1, DateTime.UtcNow.AddDays(4) }));
        }

        presentData.Add(DataObject(0, await ReadUserAsync(connection, userId.Value)));
        return presentData.ToArray();
    }

    public async Task<object?[]> UpdateHomeDisplayPreferenceAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null) return new object?[] { 0, 101, null, 101, 101, 101, 11, null, 11, 11, 11, DefaultCharacterMasterId, false, 0, 101, 11 };
        if (payload is not object?[] values || values.Length < 15)
        {
            await EnsureDefaultUserDataAsync(userId.Value);
            await using var readConnection = await database.OpenConnectionAsync();
            return await ReadHomeDisplayPreferenceAsync(readConnection, userId.Value);
        }

        await EnsureDefaultUserDataAsync(userId.Value);
        var characterBaseMasterId = GetLong(values, 0) ?? DefaultCharacterBaseMasterId;
        var illustrationCharacterBaseMasterId = GetNullableLong(values, 1);
        var subCharacterBaseMasterId1 = GetLong(values, 2) ?? DefaultCharacterBaseMasterId;
        var subCharacterBaseMasterId2 = GetLong(values, 3) ?? DefaultCharacterBaseMasterId;
        var subCharacterBaseMasterId3 = GetLong(values, 4) ?? DefaultCharacterBaseMasterId;
        var costumeMasterId = GetLong(values, 5) ?? DefaultCostumeMasterId;
        var illustrationCostumeMasterId = GetNullableLong(values, 6);
        var subCostumeMasterId1 = GetNullableLong(values, 7);
        var subCostumeMasterId2 = GetNullableLong(values, 8);
        var subCostumeMasterId3 = GetNullableLong(values, 9);
        var characterMasterId = GetLong(values, 10) ?? DefaultCharacterMasterId;
        var isIllust = GetBool(values, 11) ?? false;
        var displayType = (int)(GetLong(values, 12) ?? 0);
        var selectedCharacterBaseMasterId = GetLong(values, 13) ?? characterBaseMasterId;
        var selectedCostumeMasterId = GetLong(values, 14) ?? costumeMasterId;

        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into user_home_display_preferences (
              "userId",
              character_base_master_id,
              illustration_character_base_master_id,
              sub_character_base_master_id_1,
              sub_character_base_master_id_2,
              sub_character_base_master_id_3,
              costume_master_id,
              illustration_costume_master_id,
              sub_costume_master_id_1,
              sub_costume_master_id_2,
              sub_costume_master_id_3,
              character_master_id,
              is_illust,
              display_type,
              selected_character_base_master_id,
              selected_costume_master_id
            )
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16)
            on conflict ("userId") do update set
              character_base_master_id = excluded.character_base_master_id,
              illustration_character_base_master_id = excluded.illustration_character_base_master_id,
              sub_character_base_master_id_1 = excluded.sub_character_base_master_id_1,
              sub_character_base_master_id_2 = excluded.sub_character_base_master_id_2,
              sub_character_base_master_id_3 = excluded.sub_character_base_master_id_3,
              costume_master_id = excluded.costume_master_id,
              illustration_costume_master_id = excluded.illustration_costume_master_id,
              sub_costume_master_id_1 = excluded.sub_costume_master_id_1,
              sub_costume_master_id_2 = excluded.sub_costume_master_id_2,
              sub_costume_master_id_3 = excluded.sub_costume_master_id_3,
              character_master_id = excluded.character_master_id,
              is_illust = excluded.is_illust,
              display_type = excluded.display_type,
              selected_character_base_master_id = excluded.selected_character_base_master_id,
              selected_costume_master_id = excluded.selected_costume_master_id
            """,
            connection);
        command.Parameters.AddWithValue(userId.Value);
        command.Parameters.AddWithValue(characterBaseMasterId);
        command.Parameters.AddWithValue((object?)illustrationCharacterBaseMasterId ?? DBNull.Value);
        command.Parameters.AddWithValue(subCharacterBaseMasterId1);
        command.Parameters.AddWithValue(subCharacterBaseMasterId2);
        command.Parameters.AddWithValue(subCharacterBaseMasterId3);
        command.Parameters.AddWithValue(costumeMasterId);
        command.Parameters.AddWithValue((object?)illustrationCostumeMasterId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)subCostumeMasterId1 ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)subCostumeMasterId2 ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)subCostumeMasterId3 ?? DBNull.Value);
        command.Parameters.AddWithValue(characterMasterId);
        command.Parameters.AddWithValue(isIllust);
        command.Parameters.AddWithValue(displayType);
        command.Parameters.AddWithValue(selectedCharacterBaseMasterId);
        command.Parameters.AddWithValue(selectedCostumeMasterId);
        await command.ExecuteNonQueryAsync();

        return new object?[]
        {
            userId.Value,
            values[0],
            values[1],
            values[2],
            values[3],
            values[4],
            values[5],
            values[6],
            values[7],
            values[8],
            values[9],
            values[10],
            values[11],
            values[12],
            values[13],
            values[14]
        };
    }

    static IEnumerable<long> ExtractLongs(object? value)
    {
        switch (value)
        {
            case long longValue:
                yield return longValue;
                yield break;
            case int intValue:
                yield return intValue;
                yield break;
            case short shortValue:
                yield return shortValue;
                yield break;
            case byte byteValue:
                yield return byteValue;
                yield break;
            case string text when long.TryParse(text, out var parsed):
                yield return parsed;
                yield break;
            case object?[] array:
                foreach (var item in array)
                {
                    foreach (var number in ExtractLongs(item))
                    {
                        yield return number;
                    }
                }
                yield break;
        }
    }

    static object?[] BuildUserProfileDetail(
        string? publicId,
        string displayName,
        long mainCharacterMasterId,
        int mainCharacterLevel,
        bool characterDisplayAwakeningStatus,
        long iconFrameMasterId,
        long nameColorMasterId,
        long nameBaseColorMasterId,
        long? nameplateMasterId)
    {
        return new object?[]
        {
            publicId ?? "0000000000",
            displayName,
            null,
            null,
            null,
            mainCharacterMasterId,
            mainCharacterLevel,
            characterDisplayAwakeningStatus,
            iconFrameMasterId,
            nameColorMasterId,
            nameBaseColorMasterId,
            nameplateMasterId,
            null
        };
    }
}
