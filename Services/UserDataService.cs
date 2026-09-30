using System.Text;
using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    const long DefaultHomeSkinMasterId = 200001L;
    const long DefaultNameBaseColorMasterId = 180001L;
    const long DefaultNameColorMasterId = 1L;
    const long DefaultIconFrameMasterId = 190001L;
    const long DefaultCharacterBaseMasterId = 101L;
    const long DefaultCharacterMasterId = 110010L;
    const long DefaultCharacterBaseId = 1L;
    const long DefaultCharacterId = 1L;
    const long DefaultCostumeId = 1L;
    const long DefaultCostumeMasterId = 11L;
    const long DefaultHomeBgmMasterId = 1L;
    const long DefaultHomeBgmDetailMasterId = 1001L;
    const long DefaultPartyId = 1L;
    const long DefaultPartySlotId = 1L;
    static readonly TimeSpan LocalTimeOffset = TimeSpan.FromHours(8);

    static readonly LoginBonusReward[] LoginBonusRewards =
    {
        new(168, "「最終章」追加記念アクターガチャ10連チケットログインボーナス", 1, 210446, 1, 10, false, 30, 2, new DateTimeOffset(2026, 7, 21, 1, 0, 0, LocalTimeOffset), new DateTimeOffset(2026, 8, 10, 13, 0, 0, LocalTimeOffset)),
        new(169, "「最終章」追加記念ポスターガチャ10連チケットログインボーナス", 1, 211432, 1, 10, false, 40, 2, new DateTimeOffset(2026, 7, 21, 1, 0, 0, LocalTimeOffset), new DateTimeOffset(2026, 8, 10, 13, 0, 0, LocalTimeOffset)),
        new(170, "「最終章」追加記念ログインボーナス", 13, 0, 1000, 7, false, 50, 2, new DateTimeOffset(2026, 7, 21, 1, 0, 0, LocalTimeOffset), new DateTimeOffset(2026, 8, 10, 13, 0, 0, LocalTimeOffset)),
        new(179, "3rd Anniversaryカウントダウンログインボーナス", 1, 210001, 3, 1, false, 1, 2, new DateTimeOffset(2026, 7, 22, 8, 0, 0, LocalTimeOffset), new DateTimeOffset(2026, 7, 23, 8, 0, 0, LocalTimeOffset)),
        new(10006, "ログインボーナス", 13, 0, 300, 15, true, 1000, 1, new DateTimeOffset(2026, 7, 17, 23, 0, 0, LocalTimeOffset), DateTimeOffset.MaxValue),
        new(35013, "最終章 ログインボーナス", 1, 111103, 5, 7, false, 100, 2, new DateTimeOffset(2026, 7, 21, 1, 0, 0, LocalTimeOffset), new DateTimeOffset(2026, 7, 31, 13, 0, 0, LocalTimeOffset))
    };

    readonly PostgresDatabase database;

    public UserDataService(PostgresDatabase database)
    {
        this.database = database;
    }

    public async Task<object?[]> GetUserDataAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null)
        {
            return GameResults.UserData();
        }

        await EnsureDefaultUserDataAsync(userId.Value);
        return await ReadUserDataAsync(userId.Value);
    }

    public async Task<object?[]> GetCurrentUserResultAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null) return new object?[] { GameResults.UserData()[0] };

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        return new object?[] { await ReadUserAsync(connection, userId.Value) };
    }

    public async Task<object?[]> GetLoginPresentDataAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null) return new object?[] { GameResults.UserData()[0] };

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        var presentData = new List<object?>();
        presentData.AddRange(BuildLoginMissionRefreshPresentData());
        presentData.Add(DataObject(0, await ReadUserAsync(connection, userId.Value)));
        presentData.Add(DataObject(109, await ReadDailyLimitAsync(connection, userId.Value)));
        return presentData.ToArray();
    }

    public Task UpdateCapedPlayerRankAnnounceAsync(HttpContext context)
    {
        return Task.CompletedTask;
    }

    static string NormalizeNotificationBody(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
        {
            return "[]";
        }

        if (trimmed.StartsWith('['))
        {
            return body;
        }

        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                return document.RootElement.ValueKind == JsonValueKind.Object
                    ? JsonSerializer.Serialize(new[] { document.RootElement })
                    : "[]";
            }
            catch (JsonException)
            {
                // Fall through and treat malformed JSON as plain notification text.
            }
        }

        return JsonSerializer.Serialize(new[]
        {
            new NotificationBodyContent(4, body)
        });
    }

    readonly record struct NotificationBodyContent(int Element, string Data);

    public static async Task InsertDefaultUserDataAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, string name)
    {
        var characterBaseId = UserScopedId(userId, DefaultCharacterBaseId);
        var characterId = UserScopedId(userId, DefaultCharacterId);
        var costumeId = UserScopedId(userId, DefaultCostumeId);

        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_game_states (
              "userId", stamina, max_stamina, free_jewel, paid_jewel, coin,
              tutorial_status, is_tutorial_finished
            )
            values ($1, 200, 50, 0, 0, 0, 99, true)
            on conflict ("userId") do nothing
            """,
            userId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_profiles (
              "userId",
              display_name,
              comment,
              favorite_character_master_id,
              home_character_master_id,
              home_skin_master_id,
              name_base_color_master_id,
              name_color_master_id,
              icon_frame_master_id,
              is_public,
              accepts_friend_requests,
              show_home_character
            )
            values ($1, $2, 'よろしくお願いします。', 110010, 110010, 200001, 180001, 1, 190001, false, false, false)
            on conflict ("userId") do update set
              name_base_color_master_id = case
                when user_profiles.name_base_color_master_id <= 0 then 180001
                else user_profiles.name_base_color_master_id
              end,
              name_color_master_id = case
                when user_profiles.name_color_master_id <= 0 then 1
                else user_profiles.name_color_master_id
              end,
              icon_frame_master_id = case
                when coalesce(user_profiles.icon_frame_master_id, 0) <= 0 then 190001
                else user_profiles.icon_frame_master_id
              end
            """,
            userId,
            name);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_home_display_preferences (
              "userId",
              character_base_master_id,
              sub_character_base_master_id_1,
              sub_character_base_master_id_2,
              sub_character_base_master_id_3,
              costume_master_id,
              sub_costume_master_id_1,
              sub_costume_master_id_2,
              sub_costume_master_id_3,
              character_master_id,
              selected_character_base_master_id,
              selected_costume_master_id
            )
            values ($1, 101, 101, 101, 101, 11, 11, 11, 11, 110010, 101, 11)
            on conflict ("userId") do nothing
            """,
            userId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_character_bases (
              id,
              "userId",
              character_base_master_id,
              costume_master_id,
              selected_character_id
            )
            values ($1, $2, 101, 11, $3)
            on conflict (id) do nothing
            """,
            characterBaseId,
            userId,
            characterId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_character_cards (
              id,
              "userId",
              character_master_id,
              character_base_id,
              skill_level
            )
            values ($1, $2, 110010, $3, 1)
            on conflict (id) do nothing
            """,
            characterId,
            userId,
            characterBaseId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_character_costumes (id, "userId", costume_master_id)
            values ($1, $2, 11)
            on conflict (id) do nothing
            """,
            costumeId,
            userId);

        for (var i = 1; i < BootstrapCharacterBaseMasterIds.Length; i++)
        {
            var baseMasterId = BootstrapCharacterBaseMasterIds[i];
            var cardMasterId = BootstrapCharacterMasterIds[i];
            var defaultCostumeId = BootstrapCostumeMasterIds[i];
            var baseId = UserScopedId(userId, 2001 + i);
            var cardId = UserScopedId(userId, 1001 + i);
            await ExecuteAsync(connection, transaction,
                """
                insert into user_character_bases
                  (id, "userId", character_base_master_id, costume_master_id, selected_character_id)
                values ($1, $2, $3, $4, $5)
                on conflict ("userId", character_base_master_id) do nothing
                """,
                baseId, userId, baseMasterId, defaultCostumeId, cardId);
            await using var findBase = new NpgsqlCommand(
                """
                select id from user_character_bases
                where "userId" = $1 and character_base_master_id = $2
                """, connection, transaction);
            findBase.Parameters.AddWithValue(userId);
            findBase.Parameters.AddWithValue(baseMasterId);
            var persistedBaseId = (long)(await findBase.ExecuteScalarAsync()
                ?? throw new InvalidOperationException($"角色基础 {baseMasterId} 保存后未找到"));
            await ExecuteAsync(connection, transaction,
                """
                insert into user_character_cards
                  (id, "userId", character_master_id, character_base_id)
                values ($1, $2, $3, $4)
                on conflict ("userId", character_master_id) do nothing
                """,
                cardId, userId, cardMasterId, persistedBaseId);
            await ExecuteAsync(connection, transaction,
                """
                insert into user_character_costumes (id, "userId", costume_master_id)
                values ($1, $2, $3)
                on conflict ("userId", costume_master_id) do nothing
                """,
                UserScopedId(userId, 6001 + i), userId, defaultCostumeId);
            await ExecuteAsync(connection, transaction,
                """
                update user_character_bases as character_base
                set selected_character_id = character_card.id
                from user_character_cards as character_card
                where character_base."userId" = $1
                  and character_base.character_base_master_id = $2
                  and character_card."userId" = $1
                  and character_card.character_master_id = $3
                  and character_card.character_base_id = character_base.id
                  and character_base.selected_character_id is distinct from character_card.id
                """,
                userId, baseMasterId, cardMasterId);
        }
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_item_currencies ("userId", free_jewel, paid_jewel, coin)
            values ($1, 0, 0, 0)
            on conflict ("userId") do nothing
            """,
            userId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_home_bgms ("userId", home_bgm_master_id, selection_type, home_bgm_detail_master_id)
            values ($1, 1, 1, 1001)
            on conflict ("userId") do nothing
            """,
            userId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_home_skins ("userId", selected_home_skin_master_id)
            values ($1, 200001)
            on conflict ("userId") do nothing
            """,
            userId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_home_skin_possessions ("userId", home_skin_master_id)
            values ($1, 200001)
            on conflict ("userId", home_skin_master_id) do nothing
            """,
            userId);

        var selectedPartyId = UserScopedId(userId, 4001);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_player_preferences ("userId", selected_party_id)
            values ($1, $2)
            on conflict ("userId") do nothing
            """,
            userId,
            selectedPartyId);

        for (var i = 0; i < BootstrapCharacterBaseMasterIds.Length; i++)
        {
            var characterBaseMasterId = BootstrapCharacterBaseMasterIds[i];
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into user_character_resource_progress (
                  id, "userId", character_base_master_id, resource_type, resource_master_id,
                  level, exp, rank, progress
                )
                values ($1, $2, $3, 3, 10201, 1, 0, 0, 0)
                on conflict ("userId", character_base_master_id, resource_type) do nothing
                """,
                UserScopedId(userId, 3000 + (i * 2)),
                userId,
                characterBaseMasterId);

            if (characterBaseMasterId == 401) continue;
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into user_character_resource_progress (
                  id, "userId", character_base_master_id, resource_type, resource_master_id,
                  level, exp, rank, progress
                )
                values ($1, $2, $3, 5, 10205, 1, 0, 0, 0)
                on conflict ("userId", character_base_master_id, resource_type) do nothing
                """,
                UserScopedId(userId, 3001 + (i * 2)),
                userId,
                characterBaseMasterId);
        }

        for (var i = 0; i < 12; i++)
        {
            var partyId = UserScopedId(userId, 4000 + i + 1);
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into user_party_groups (id, "userId", display_index, name, party_type)
                values ($1, $2, $3, $4, 1)
                on conflict (id) do nothing
                """,
                partyId,
                userId,
                i,
                $"グループ{i + 1}");

            for (var slot = 1; slot <= 5; slot++)
            {
                var characterIndex = slot switch
                {
                    1 => 0,
                    2 => 2,
                    3 => 3,
                    4 => 4,
                    _ => 5
                };
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    insert into user_party_slots (
                      id, "userId", party_id, slot_number, character_card_id, accessory_id, poster_id, position
                    )
                    values ($1, $2, $3, $4, $5, null, null, 0)
                    on conflict (id) do nothing
                    """,
                    UserScopedId(userId, 5000 + (i * 10) + slot),
                    userId,
                    partyId,
                    slot,
                    UserScopedId(userId, 1000 + characterIndex + 1));
            }
        }

        for (var i = 0; i < BootstrapMissionStates.Length; i++)
        {
            var state = BootstrapMissionStates[i];
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into user_mission_statuses (
                  id, "userId", is_cleared, is_received, progress, completed_at, mission_from, mission_to
                )
                values ($1, $2, $3, false, $4, null, $5, $6)
                on conflict ("userId", mission_from, mission_to) do nothing
                """,
                UserScopedId(userId, 8001 + i),
                userId,
                state.Cleared,
                state.Progress,
                state.From,
                state.To);
        }
        await ExecuteAsync(connection, transaction,
            """
            update user_mission_statuses
            set is_cleared = false, is_received = false, progress = 0,
                completed_at = null, updated_at = now()
            where "userId" = $1
              and mission_from between 100000 and 100499
              and updated_at < $2
            """,
            userId, CurrentDailyResetBoundaryUtc(DateTimeOffset.UtcNow).UtcDateTime);

        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_live_user_stats ("userId", score_rating, technical_rating)
            values ($1, 0, 0)
            on conflict ("userId") do nothing
            """,
            userId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_player_settings (id, "userId", display_quality, download_category, is_push_enabled, is_live_push_enabled)
            values ($1, $2, 0, 36, false, false)
            on conflict ("userId") do nothing
            """,
            UserScopedId(userId, 9701),
            userId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_restriction_states ("userId", restricted_until, reason)
            values ($1, null, null)
            on conflict ("userId") do nothing
            """,
            userId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_invite_states ("userId", invited_count, invite_code, is_reward_received)
            values ($1, 0, $2, false)
            on conflict ("userId") do nothing
            """,
            userId,
            $"LOCAL{userId % 100000:D5}");
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_login_bonus_states (id, "userId", current_count, total_count, shown_at, status)
            values ($1, $2, 0, 0, now(), 0)
            on conflict ("userId") do nothing
            """,
            UserScopedId(userId, 10901),
            userId);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_daily_limits (id, "userId", auto_play_times, daily_lesson_times, last_refreshed_at, music_course_free_challenge_times)
            values ($1, $2, 0, 0, $3, 0)
            on conflict ("userId") do nothing
            """,
            UserScopedId(userId, 10902),
            userId,
            CurrentDailyResetBoundaryUtc(DateTimeOffset.UtcNow).UtcDateTime);
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_notification_states ("userId", notification_read_at, notice_read_at, present_read_at)
            values ($1, null, null, null)
            on conflict ("userId") do nothing
            """,
            userId);
        for (var i = 1; i <= 5; i++)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into user_album_groups ("userId", album_index, name, display_order)
                values ($1, $2, $3, $2)
                on conflict ("userId", album_index) do nothing
                """,
                userId,
                i,
                $"アルバム{i}");
        }

        for (var i = 0; i < BootstrapInboxRewards.Length; i++)
        {
            var reward = BootstrapInboxRewards[i];
            var bootstrapKey = $"registration-{i + 1}";
            var payload = JsonSerializer.Serialize(new
            {
                bootstrapKey,
                reward.ThingType,
                reward.MasterId,
                reward.Quantity,
                reward.Message
            });
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into user_item_inbox_packages ("userId", payload, expires_at)
                select $1, $2::jsonb, now() + interval '30 days'
                where not exists (
                  select 1
                  from user_item_inbox_packages
                  where "userId" = $1
                    and payload ->> 'bootstrapKey' = $3
                )
                """,
                userId,
                payload,
                bootstrapKey);
        }
    }

    async Task EnsureDefaultUserDataAsync(long userId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var name = await ReadUserNameAsync(connection, transaction, userId) ?? "LocalPlayer";
            await InsertDefaultUserDataAsync(connection, transaction, userId, name);
            await NormalizeRankAsync(connection, transaction, userId);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    async Task UpdateUserGameStateTimestampAsync(HttpContext context, string columnName)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return;

        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"""
            update user_game_states
            set {columnName} = now(),
                updated_at = now()
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId.Value);
        await command.ExecuteNonQueryAsync();
    }

    async Task<object?[]> ReadUserDataAsync(long userId)
    {
        await using var connection = await database.OpenConnectionAsync();
        var user = await ReadUserAsync(connection, userId);
        var profile = await ReadProfileAsync(connection, userId);
        var homeDisplayPreference = await ReadHomeDisplayPreferenceAsync(connection, userId);
        var characters = await ReadCharactersAsync(connection, userId);
        var characterBases = await ReadCharacterBasesAsync(connection, userId);
        var costumes = await ReadCostumesAsync(connection, userId);
        var currency = await ReadCurrencyAsync(connection, userId);
        var homeBgm = await ReadHomeBgmAsync(connection, userId);
        var homeSkin = await ReadHomeSkinAsync(connection, userId);
        var userPreference = await ReadUserPreferenceAsync(connection, userId);
        var itemPossessions = await ReadItemPossessionsAsync(connection, userId);
        var livePlayResults = await ReadLivePlayResultsAsync(connection, userId);
        var anotherNotationResults = await ReadAnotherNotationResultsAsync(connection, userId);
        var liveMusicStates = await ReadLiveMusicStatesAsync(connection, userId);
        var dugongRunStates = await ReadDugongRunStatesAsync(connection, userId);
        var parties = await ReadPartiesAsync(connection, userId);
        var partySlots = await ReadPartySlotsAsync(connection, userId);
        var lessonParties = await ReadLessonPartiesAsync(connection, userId);
        var notification = await ReadNotificationAsync(connection, userId);
        await using (var cleanupCommand = new NpgsqlCommand(
            """
            delete from user_item_inbox_packages
            where "userId" = $1
              and (
                payload ->> 'bootstrapKey' like 'login-bonus-20260928-%'
                or payload ->> 'bootstrapKey' like 'check-package-%'
              )
            """,
            connection))
        {
            cleanupCommand.Parameters.AddWithValue(userId);
            await cleanupCommand.ExecuteNonQueryAsync();
        }
        var inboxPackages = await ReadInboxPackagesAsync(connection, userId);
        var characterResourceProgress = await ReadCharacterMissionsAsync(connection, userId);
        var missions = await ReadMissionsAsync(connection, userId);
        var liveUserStats = await ReadLiveUserStatsAsync(connection, userId);
        var musicBookmarks = await ReadMusicBookmarksAsync(connection, userId);
        var gameHintReads = await ReadGameHintReadsAsync(connection, userId);
        var shopLastViews = await ReadShopLastViewsAsync(connection, userId);
        var episodeReadStates = await ReadEpisodeReadStatesAsync(connection, userId);
        var playerSetting = await ReadPlayerSettingAsync(connection, userId);
        var restriction = await ReadRestrictionAsync(connection, userId);
        var albums = await ReadAlbumsAsync(connection, userId);
        var photoRecords = await ReadPhotoRecordsAsync(connection, userId);
        var photoStates = await ReadPhotoStatesAsync(connection, userId);
        var photoAlbumArrangements = await ReadPhotoAlbumArrangementsAsync(connection, userId);
        var musicVideoWatchStates = await ReadMusicVideoWatchStatesAsync(connection, userId);
        var dailyLimit = await ReadDailyLimitAsync(connection, userId);
        var inviteState = await ReadInviteStateAsync(connection, userId);

        var result = new List<object?>
        {
            DataObject(0, user),
            DataObject(1, profile),
            DataObject(2, userPreference),
            DataObject(3, homeDisplayPreference)
        };
        result.AddRange(characters.Select(value => DataObject(4, value)));
        result.AddRange(characterBases.Select(value => DataObject(5, value)));
        result.AddRange(parties.Select(value => DataObject(6, value)));
        result.AddRange(partySlots.Select(value => DataObject(7, value)));
        result.AddRange(lessonParties.Select(value => DataObject(39, value)));
        result.AddRange(costumes.Select(value => DataObject(43, value)));
        result.AddRange(itemPossessions.Select(value => DataObject(27, value)));
        result.AddRange(livePlayResults.Select(value => DataObject(24, value)));
        result.AddRange(anotherNotationResults.Select(value => DataObject(AnotherNotationUnionKey, value)));
        result.AddRange(liveMusicStates.Select(value => DataObject(25, value)));
        result.AddRange(dugongRunStates.Select(value => DataObject(177, value)));
        result.AddRange(characterResourceProgress.Select(value => DataObject(96, value)));
        result.Add(DataObject(94, notification));
        result.Add(DataObject(97, playerSetting));
        result.AddRange(missions.Select(value => DataObject(48, value)));
        result.Add(DataObject(67, liveUserStats));
        result.Add(DataObject(109, dailyLimit));
        result.Add(DataObject(128, currency));
        result.AddRange(musicBookmarks.Select(value => DataObject(146, value)));
        result.AddRange(gameHintReads.Select(value => DataObject(66, value)));
        result.AddRange(shopLastViews.Select(value => DataObject(65, value)));
        result.AddRange(episodeReadStates.Select(value => DataObject(95, value)));
        result.Add(DataObject(148, restriction));
        result.Add(DataObject(161, homeBgm));
        result.AddRange(photoRecords.Select(value => DataObject(123, value)));
        result.AddRange(photoStates.Select(value => DataObject(124, value)));
        result.AddRange(photoAlbumArrangements.Select(value => DataObject(125, value)));
        result.AddRange(albums.Select(value => DataObject(174, value)));
        result.AddRange(musicVideoWatchStates.Select(value => DataObject(193, value)));
        result.Add(DataObject(179, inviteState));
        result.Add(DataObject(189, homeSkin));
        result.AddRange(inboxPackages.Select(value => DataObject(41, value)));
        AddOfficialBootstrapData(result, userId);
        return BuildOfficialUserDataBatches(result, userId);
    }

    static object?[] BuildOfficialUserDataBatches(List<object?> source, long userId)
    {
        // The official /api/data/user payload is split into logical batches with
        // nil entries between them.  These nils are significant to the client-side
        // data synchronizer; returning one flat list can leave the Home bootstrap
        // task waiting even though the HTTP request itself completed successfully.
        // Within each batch the union keys are also ordered.  The generated
        // bootstrap data is deliberately added piecemeal, so preserving source
        // order here produces a valid MessagePack document but not the stream
        // expected by the client's data synchronizer.
        int[] inventoryTypeOrder = { 120, 4, 5, 96, 43, 101, 177, 179, 139, 183, 94, 150, 41, 27, 100, 111, 106, 107, 168, 171, 170, 176, 39, 24, 25, 90, 147, 109 };
        int[] missionTypeOrder = { 145, 146, 97, 48, 122, 154, 193, 6, 7, 166, 167, 123, 124, 125, 131, 174, 0, 1, 3, 2, 66, 67, 128, 138, 148 };
        int[] accountTypeOrder = { 47, 45, 11, 65, 64, 102 };
        int[] shopTypeOrder = { 108, 149, 95 };

        static int? UnionKey(object? item)
        {
            return item is object?[] { Length: >= 2 } dataObject && dataObject[0] is int key
                ? key
                : null;
        }

        static object?[]? UnionValue(object? item)
        {
            return item is object?[] { Length: >= 2 } dataObject
                ? dataObject[1] as object?[]
                : null;
        }

        static long? NumericId(object? value)
        {
            return value switch
            {
                byte number => number,
                short number => number,
                int number => number,
                long number => number,
                uint number => number,
                ulong number when number <= long.MaxValue => (long)number,
                _ => null
            };
        }

        var result = new List<object?>();
        var bootstrapPartyIds = Enumerable.Range(0, 12)
            .Select(index => UserScopedId(userId, 4001 + index))
            .ToHashSet();

        IEnumerable<object?> ItemsOfType(int unionKey)
        {
            var items = source.Where(item => UnionKey(item) == unionKey);

            // The database seed contributes one legacy "Party 1" row in
            // addition to the twelve official-style groups.  Sending all
            // thirteen leaves the party cache inconsistent with its members.
            if (unionKey == 6)
            {
                items = items.Where(item =>
                {
                    var value = UnionValue(item);
                    return value is { Length: > 0 } &&
                           NumericId(value[0]) is long partyId &&
                           bootstrapPartyIds.Contains(partyId);
                });
            }
            else if (unionKey == 7)
            {
                items = items.Where(item =>
                {
                    var value = UnionValue(item);
                    return value is { Length: > 1 } &&
                           NumericId(value[1]) is long partyId &&
                           bootstrapPartyIds.Contains(partyId);
                });
            }

            return items;
        }

        void AddTypesInOrder(IEnumerable<int> unionKeys)
        {
            foreach (var unionKey in unionKeys)
            {
                result.AddRange(ItemsOfType(unionKey));
            }
        }

        // 2.31.x 会把饰品对象作为独立的首批数据发送；没有饰品时该批为空。
        AddTypesInOrder(new[] { 26 });
        result.Add(null);

        AddTypesInOrder(inventoryTypeOrder);
        result.Add(null);
        AddTypesInOrder(missionTypeOrder);
        result.Add(null);

        AddTypesInOrder(accountTypeOrder);
        result.Add(null);

        AddTypesInOrder(shopTypeOrder);

        return result.ToArray();
    }

    static void AddOfficialBootstrapData(List<object?> result, long userId)
    {
        // 数据库存档优先；预设角色只补缺，默认队伍必须引用实际同步的卡片 ID。
        var ownedCards = result.OfType<object?[]>()
            .Where(item => item[0] is 4).Select(item => (object?[])item[1]!)
            .ToDictionary(value => Convert.ToInt64(value[1]));
        var ownedBases = result.OfType<object?[]>()
            .Where(item => item[0] is 5).Select(item => (object?[])item[1]!)
            .ToDictionary(value => Convert.ToInt64(value[1]));
        var bootstrapPartyCharacters = new long[BootstrapCharacterMasterIds.Length];
        var existing = new HashSet<string>();
        foreach (var item in result.OfType<object?[]>())
        {
            if (item.Length < 2 || item[0] is not int unionKey || item[1] is not object?[] value || value.Length == 0) continue;
            existing.Add(DataObjectIdentity(unionKey, value));
        }

        void AddIfMissing(int unionKey, object?[] value)
        {
            if (existing.Add(DataObjectIdentity(unionKey, value)))
            {
                result.Add(DataObject(unionKey, value));
            }
        }

        for (var i = 0; i < BootstrapCharacterBaseMasterIds.Length; i++)
        {
            var characterBaseMasterId = BootstrapCharacterBaseMasterIds[i];
            var characterMasterId = BootstrapCharacterMasterIds[i];
            var costumeMasterId = BootstrapCostumeMasterIds[i];
            var hasCard = ownedCards.TryGetValue(characterMasterId, out var ownedCard);
            var hasBase = ownedBases.TryGetValue(characterBaseMasterId, out var ownedBase);
            var characterId = hasCard ? Convert.ToInt64(ownedCard![0]) : UserScopedId(userId, 1000 + i + 1);
            var characterBaseId = hasBase ? Convert.ToInt64(ownedBase![0]) : UserScopedId(userId, 2000 + i + 1);
            bootstrapPartyCharacters[i] = characterId;

            if (!hasCard) AddIfMissing(4, new object?[]
            {
                characterId,
                characterMasterId,
                1,
                0,
                0,
                0,
                characterBaseId,
                1,
                0,
                0,
                false,
                null,
                0,
                1,
                false
            });

            if (!hasBase) AddIfMissing(5, new object?[]
            {
                characterBaseId,
                characterBaseMasterId,
                0,
                0,
                costumeMasterId,
                0,
                // PortalCharacterId 必须引用玩家卡片实例，不能引用 CharacterMaster 的主数据 ID。
                characterId,
                false
            });

        }

        for (var i = 0; i < 12; i++)
        {
            var partyId = UserScopedId(userId, 4000 + i + 1);
            AddIfMissing(6, new object?[] { partyId, i, $"グループ{i + 1}", 1 });
            AddIfMissing(166, new object?[] { i, i, $"グループ{i + 1}", 1 });

            for (var slot = 1; slot <= 5; slot++)
            {
                var characterIndex = slot switch
                {
                    1 => 0,
                    2 => 2,
                    3 => 3,
                    4 => 4,
                    _ => 5
                };
                AddIfMissing(7, new object?[]
                {
                    UserScopedId(userId, 5000 + (i * 10) + slot),
                    partyId,
                    slot,
                    bootstrapPartyCharacters[characterIndex],
                    null,
                    null,
                    0
                });
                AddIfMissing(167, new object?[]
                {
                    (i * 5) + slot,
                    i,
                    slot,
                    bootstrapPartyCharacters[characterIndex],
                    null,
                    null,
                    0
                });
            }
        }

        for (var i = 0; i < BootstrapMissionStates.Length; i++)
        {
            var state = BootstrapMissionStates[i];
            AddIfMissing(48, new object?[]
            {
                UserScopedId(userId, 8001 + i),
                state.Cleared,
                false,
                state.Progress,
                null,
                state.From,
                state.To
            });
        }

        // 旧存档的默认队伍可能仍保存预设 ID，同步时对齐数据库卡片 ID。
        var characterAliases = Enumerable.Range(0, bootstrapPartyCharacters.Length)
            .ToDictionary(index => UserScopedId(userId, 1001 + index), index => bootstrapPartyCharacters[index]);
        foreach (var item in result.OfType<object?[]>())
        {
            if (item[0] is not (7 or 167) || item[1] is not object?[] slot || slot.Length <= 3 || slot[3] is null) continue;
            if (characterAliases.TryGetValue(Convert.ToInt64(slot[3]), out var actualId)) slot[3] = actualId;
        }

        AddIfMissing(67, new object?[] { userId, 0.0, 0.0 });
        AddIfMissing(97, BuildInitialPlayerSetting(userId));
        AddIfMissing(109, new object?[] { UserScopedId(userId, 10902), 0, 0, CurrentDailyResetBoundaryUtc(DateTimeOffset.UtcNow).UtcDateTime, 0 });
        AddIfMissing(148, new object?[] { userId, null, true });
        AddIfMissing(174, new object?[] { 1, "アルバム1", 1 });
        AddIfMissing(174, new object?[] { 2, "アルバム2", 2 });
        AddIfMissing(174, new object?[] { 3, "アルバム3", 3 });
        AddIfMissing(174, new object?[] { 4, "アルバム4", 4 });
        AddIfMissing(174, new object?[] { 5, "アルバム5", 5 });
        AddIfMissing(179, BuildInviteCode(userId));
    }

    static object?[] BuildInviteCode(long userId)
    {
        return new object?[] { 0, $"LOCAL{userId % 100000:D5}", false };
    }

    static object?[] BuildInitialPlayerSetting(long userId)
    {
        return new object?[] { UserScopedId(userId, 9701), 0, 36, false, null, null, false, null, null, null, null };
    }

    static string DataObjectIdentity(int unionKey, object?[] value)
    {
        return unionKey switch
        {
            27 => $"{unionKey}:{(value.Length > 1 ? value[1] : value[0])}",
            96 => $"{unionKey}:{(value.Length > 1 ? value[1] : value[0])}:{(value.Length > 2 ? value[2] : 0)}",
            7 => $"{unionKey}:{(value.Length > 1 ? value[1] : value[0])}:{(value.Length > 2 ? value[2] : 0)}",
            48 => $"{unionKey}:{(value.Length > 5 ? value[5] : value[0])}:{(value.Length > 6 ? value[6] : 0)}",
            174 => $"{unionKey}:{value[0]}",
            _ => $"{unionKey}:{value[0]}"
        };
    }

    static readonly long[] BootstrapCharacterBaseMasterIds =
    {
        101, 102, 103, 104, 105, 106,
        201, 202, 203, 204, 205,
        301, 302, 303, 304, 305,
        401, 402, 403, 404, 405
    };

    static readonly long[] BootstrapCharacterMasterIds =
    {
        110010, 110020, 110030, 110040, 110050, 110060,
        110070, 110080, 110090, 110100, 110110,
        110120, 110130, 110140, 110150, 110160,
        110170, 110180, 110190, 110200, 110210
    };

    static readonly long[] BootstrapCostumeMasterIds =
    {
        11, 21, 31, 211, 41, 51,
        241, 61, 71, 81, 281,
        91, 101, 111, 121, 131,
        141, 151, 161, 171, 381
    };

    static readonly (bool Cleared, int Progress, int From, int To)[] BootstrapMissionStates =
    {
        (false, 1, 7, 7),
        (false, 1, 13, 13),
        (false, 1, 19, 19),
        (false, 1, 25, 25),
        (false, 1, 31, 31),
        (false, 1, 37, 37),
        (false, 6, 120, 121),
        (false, 5, 220, 221),
        (false, 5, 320, 321),
        (false, 10, 420, 421),
        (false, 1, 1000, 1001),
        (false, 1, 1100, 1100),
        (false, 21, 1200, 1201),
        (false, 1, 1900, 1901),
        (false, 0, 4500, 4501)
    };

    const string RegistrationCampaignMessage = "事前登録キャンペーンの報酬です。";
    const string Release100DaysMessage = "リリース100日目を記念した報酬です。";
    const string WelcomePresentMessage = "ユメステを始めていただいた皆さんへのプレゼントです。";
    const string FirstAnniversaryMessage = "ユメステ1周年のアップデートを記念した、皆様へのプレゼントです！";
    const string ProducerLetterMessage = "【プロデューサーレター】をお読みいただいた皆様へ";
    const string AppStoreRankingMessage = "AppStore無料ランキング2位記念の配布アイテムです。";
    const string TrendRankingMessage = "『#ユメステ特番』のトレンド30位以内達成を記念した報酬です。";

    static readonly BootstrapInboxReward[] BootstrapInboxRewards =
    {
        new(13, 0, 3000, RegistrationCampaignMessage),
        new(1, 210001, 10, RegistrationCampaignMessage),
        new(1, 130051, 10, RegistrationCampaignMessage),
        new(1, 130043, 100, RegistrationCampaignMessage),
        new(1, 130042, 100, RegistrationCampaignMessage),
        new(1, 130041, 100, RegistrationCampaignMessage),
        new(1, 120002, 500, RegistrationCampaignMessage),
        new(1, 130064, 100, RegistrationCampaignMessage),
        new(1, 130063, 100, RegistrationCampaignMessage),
        new(1, 130062, 100, RegistrationCampaignMessage),
        new(1, 130061, 100, RegistrationCampaignMessage),
        new(13, 0, 1000, Release100DaysMessage),
        new(1, 130001, 50, WelcomePresentMessage),
        new(1, 150000, 250, FirstAnniversaryMessage),
        new(1, 151001, 10, ProducerLetterMessage),
        new(1, 151002, 10, ProducerLetterMessage),
        new(1, 151003, 10, ProducerLetterMessage),
        new(1, 151004, 10, ProducerLetterMessage),
        new(1, 151005, 10, ProducerLetterMessage),
        new(1, 151006, 10, ProducerLetterMessage),
        new(1, 151007, 10, ProducerLetterMessage),
        new(1, 151008, 10, ProducerLetterMessage),
        new(1, 151009, 10, ProducerLetterMessage),
        new(1, 151010, 10, ProducerLetterMessage),
        new(1, 151011, 10, ProducerLetterMessage),
        new(1, 151012, 10, ProducerLetterMessage),
        new(1, 151013, 10, ProducerLetterMessage),
        new(1, 151014, 10, ProducerLetterMessage),
        new(1, 151015, 10, ProducerLetterMessage),
        new(1, 151016, 10, ProducerLetterMessage),
        new(1, 151017, 10, ProducerLetterMessage),
        new(1, 151018, 10, ProducerLetterMessage),
        new(1, 151019, 10, ProducerLetterMessage),
        new(1, 151020, 10, ProducerLetterMessage),
        new(1, 151021, 10, ProducerLetterMessage),
        new(1, 210424, 1, WelcomePresentMessage)
    };

    static readonly BootstrapInboxReward[] CheckPackageInboxRewards =
    {
        new(1, 220101, 5, AppStoreRankingMessage),
        new(1, 221101, 5, AppStoreRankingMessage),
        new(13, 0, 500, TrendRankingMessage)
    };

    readonly record struct BootstrapInboxReward(int ThingType, long MasterId, int Quantity, string Message);

    async Task<object?[]> ReadUserAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select user_accounts.id,
                   user_game_states.rank,
                   user_game_states.exp,
                   user_game_states.stamina,
                   user_game_states.stamina_recovered_at,
                   user_game_states.free_jewel,
                   user_game_states.paid_jewel,
                   user_game_states.coin,
                   user_game_states.rank_limit,
                   user_game_states.stamina_jewel_recovery_count,
                   user_game_states.splash_last_displayed_at,
                   user_game_states.game_start_at,
                   user_accounts.public_id,
                   user_game_states.tutorial_status,
                   user_game_states.is_tutorial_finished,
                   user_game_states.has_unread_game_hint,
                   system_circles.display_id::text
            from user_accounts
            join user_game_states on user_game_states."userId" = user_accounts.id
            left join user_circle_memberships on user_circle_memberships."userId" = user_accounts.id
            left join system_circles on system_circles.id = user_circle_memberships.circle_id
            where user_accounts.id = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return GameResults.UserData()[0] as object?[] ?? Array.Empty<object?>();

        var tutorialStatus = NormalizeTutorialStatus(reader.GetInt32(13));
        var unsetAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        var publicId = reader.GetString(12);
        var publicNumericId = long.TryParse(publicId, out var parsedPublicId) ? parsedPublicId : reader.GetInt64(0);
        return new object?[]
        {
            publicNumericId,
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetDateTime(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            unsetAt,
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.GetDateTime(11),
            publicId,
            0,
            tutorialStatus,
            0,
            unsetAt,
            false,
            false
        };
    }

    async Task<object?[]> ReadProfileAsync(NpgsqlConnection connection, long userId)
    {
        var playerRating = await ReadPlayerRatingAsync(connection, userId);
        await using var command = new NpgsqlCommand(
            """
            select display_name,
                   comment,
                   favorite_character_id,
                   title_master_id,
                   nameplate_master_id,
                   icon_frame_master_id,
                   birthday,
                   support_character_id,
                   is_public,
                   total_power,
                   honor_point,
                   accepts_friend_requests,
                   selected_poster_id,
                   home_character_master_id,
                   is_home_character_illust,
                   show_home_character,
                   name_base_color_master_id,
                   name_color_master_id,
                   home_skin_master_id
            from user_profiles
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new object?[]
        {
            userId,
            reader.GetString(0),
            reader.GetString(1),
            GetNullableInt64(reader, 2) ?? 0L,
            GetNullableInt64(reader, 4),
            reader.GetInt64(17) > 0 ? reader.GetInt64(17) : DefaultNameColorMasterId,
            null,
            null,
            null,
            playerRating,
            null,
            reader.GetBoolean(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetBoolean(11),
            null,
            GetNullableInt64(reader, 13) ?? DefaultCharacterMasterId,
            reader.GetBoolean(14),
            reader.GetBoolean(15),
            // These resources are loaded while the title flow transitions to
            // Home.  Legacy local rows used zero/guessed IDs which serialize
            // correctly but do not exist in the 2.31.0 master database.
            reader.GetInt64(16) > 0 ? reader.GetInt64(16) : DefaultNameBaseColorMasterId,
            GetNullableInt64(reader, 5) is long iconFrameMasterId && iconFrameMasterId > 0
                ? iconFrameMasterId
                : DefaultIconFrameMasterId,
            GetNullableInt64(reader, 18) is long homeSkinMasterId && homeSkinMasterId > 0
                ? homeSkinMasterId
                : DefaultHomeSkinMasterId
        };
    }

    async Task<object?[]> ReadUserProfileDetailAsync(long userId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select user_accounts.public_id,
                   user_profiles.display_name,
                   user_profiles.home_character_master_id,
                   user_profiles.is_home_character_illust,
                   user_profiles.icon_frame_master_id,
                   user_profiles.name_color_master_id,
                   user_profiles.name_base_color_master_id,
                   user_profiles.nameplate_master_id
            from user_accounts
            join user_profiles on user_profiles."userId" = user_accounts.id
            where user_accounts.id = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return BuildUserProfileDetail(FormatNumericId(userId), "LocalPlayer", DefaultCharacterMasterId, 1, false, DefaultIconFrameMasterId, DefaultNameColorMasterId, DefaultNameBaseColorMasterId, null);
        }

        return BuildUserProfileDetail(
            reader.GetString(0),
            reader.GetString(1),
            GetNullableInt64(reader, 2) ?? DefaultCharacterMasterId,
            1,
            reader.GetBoolean(3),
            GetNullableInt64(reader, 4) ?? 0L,
            reader.GetInt64(5),
            reader.GetInt64(6),
            GetNullableInt64(reader, 7));
    }

    static async Task<int> ExecuteNonQueryAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params object[] values)
    {
        await using var command = transaction is null
            ? new NpgsqlCommand(sql, connection)
            : new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values)
        {
            command.Parameters.AddWithValue(value);
        }
        return await command.ExecuteNonQueryAsync();
    }

    async Task<object?[]> ReadHomeDisplayPreferenceAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select
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
            from user_home_display_preferences
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"用户 {userId} 缺少主页展示偏好数据");
        }

        return new object?[]
        {
            userId,
            reader.GetInt64(0),
            GetNullableInt64(reader, 1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            GetNullableInt64(reader, 6),
            GetNullableInt64(reader, 7),
            GetNullableInt64(reader, 8),
            GetNullableInt64(reader, 9),
            reader.GetInt64(10),
            reader.GetBoolean(11),
            reader.GetInt32(12),
            reader.GetInt64(13),
            reader.GetInt64(14)
        };
    }

    async Task<List<object?[]>> ReadCharactersAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select *
            from user_character_cards
            where "userId" = $1
            order by id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(reader.GetOrdinal("id")),
                reader.GetInt64(reader.GetOrdinal("character_master_id")),
                reader.GetInt32(reader.GetOrdinal("level")),
                reader.GetInt32(reader.GetOrdinal("exp")),
                0,
                reader.GetInt32(reader.GetOrdinal("awakening")),
                reader.GetInt64(reader.GetOrdinal("character_base_id")),
                reader.GetInt32(reader.GetOrdinal("skill_level")),
                reader.GetInt32(reader.GetOrdinal("extra_1")),
                reader.GetInt32(reader.GetOrdinal("extra_2")),
                false,
                null,
                0,
                1,
                false
            });
        }
        return result;
    }

    async Task<List<object?[]>> ReadCharacterBasesAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select *
            from user_character_bases
            where "userId" = $1
            order by id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(reader.GetOrdinal("id")),
                reader.GetInt64(reader.GetOrdinal("character_base_master_id")),
                reader.GetInt32(reader.GetOrdinal("level")),
                reader.GetInt32(reader.GetOrdinal("exp")),
                reader.GetInt64(reader.GetOrdinal("costume_master_id")),
                reader.GetInt32(reader.GetOrdinal("rank")),
                GetNullableInt64(reader, reader.GetOrdinal("selected_character_id")) ?? UserScopedId(userId, DefaultCharacterId),
                reader.GetBoolean(reader.GetOrdinal("is_new"))
            });
        }
        return result;
    }

    async Task<List<object?[]>> ReadCostumesAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, costume_master_id
            from user_character_costumes
            where "userId" = $1
            order by id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[] { reader.GetInt64(0), reader.GetInt64(1) });
        }
        return result;
    }

    async Task<List<object?[]>> ReadItemPossessionsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, item_master_id, quantity
            from user_item_possessions
            where "userId" = $1
            order by item_master_id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2)
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadLivePlayResultsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id,
                   live_master_id,
                   difficulty,
                   achievement_rate,
                   notation_rate,
                   clear_count,
                   clear_lamp,
                   rate_grade
            from user_live_play_results
            where "userId" = $1
            order by live_master_id, difficulty
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(5),
                null,
                GetNullableDouble(reader, 3) ?? 0.0,
                GetNullableDouble(reader, 4) ?? 0.0,
                reader.GetInt32(6),
                2,
                reader.GetInt32(7)
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadLiveMusicStatesAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, music_master_id, is_unlocked
            from user_live_music_states
            where "userId" = $1
            order by music_master_id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                null,
                reader.GetInt64(1),
                null,
                null,
                false,
                null,
                0,
                0,
                reader.GetBoolean(2)
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadDugongRunStatesAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, cleared_course_ids, no_mistake_course_ids, status
            from user_dugong_run_states
            where "userId" = $1
            order by event_master_id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                DugongRunStatePublicId,
                (int[])reader[1],
                (int[])reader[2],
                reader.GetInt32(3)
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadPhotoRecordsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id,
                   file_name,
                   read_sas_token,
                   write_sas_token,
                   is_favorite,
                   album_group_id,
                   source_type,
                   source_master_id,
                   created_at
            from user_photo_records
            where "userId" = $1
            order by created_at, id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? GameResults.BuildPhotoSasToken("photo") : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4),
                GetNullableInt64(reader, 5),
                reader.GetInt32(6),
                GetNullableInt64(reader, 7) ?? 5,
                null,
                reader.GetDateTime(8),
                GameResults.BuildPhotoSasToken("thumbnail"),
                new object?[] { 404, 104 },
                new object?[] { 404, 104 },
                0
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadPhotoStatesAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, photo_category, current_count, max_count
            from user_photo_states
            where "userId" = $1
            order by photo_category
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3)
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadPhotoAlbumArrangementsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, album_group_id, arrangement_type, is_public, layout_payload
            from user_photo_album_arrangements
            where "userId" = $1
            order by album_group_id, arrangement_type
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetBoolean(3),
                new MsgPack.Binary((byte[])reader[4]),
                null
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadMusicVideoWatchStatesAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select music_video_id
            from user_music_video_watch_states
            where "userId" = $1
            order by music_video_id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[] { reader.GetInt64(0), "" });
        }
        return result;
    }

    async Task<object?[]> ReadCurrencyAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select free_jewel, paid_jewel, coin
            from user_item_currencies
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new object?[] { userId, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2) };
    }

    async Task<object?[]> ReadHomeBgmAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select home_bgm_master_id, selection_type, home_bgm_detail_master_id
            from user_home_bgms
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new object?[] { reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2) };
    }

    async Task<object?[]> ReadHomeSkinAsync(NpgsqlConnection connection, long userId)
    {
        await using var selectedCommand = new NpgsqlCommand(
            """
            select selected_home_skin_master_id
            from user_home_skins
            where "userId" = $1
            """,
            connection);
        selectedCommand.Parameters.AddWithValue(userId);
        var selected = (long?)await selectedCommand.ExecuteScalarAsync() ?? DefaultHomeSkinMasterId;

        await using var possessionCommand = new NpgsqlCommand(
            """
            select home_skin_master_id
            from user_home_skin_possessions
            where "userId" = $1
            order by home_skin_master_id
            """,
            connection);
        possessionCommand.Parameters.AddWithValue(userId);

        var possessed = new List<long>();
        await using var reader = await possessionCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            possessed.Add(reader.GetInt64(0));
        }

        if (possessed.Count == 0) possessed.Add(DefaultHomeSkinMasterId);
        return new object?[] { selected, possessed.ToArray() };
    }

    static async Task<object?[]> ReadUserPreferenceAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select selected_party_id
            from user_player_preferences
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);
        var selectedPartyId = await command.ExecuteScalarAsync() as long? ?? UserScopedId(userId, 4001);
        return new object?[] { userId, selectedPartyId, null };
    }

    static async Task<List<object?[]>> ReadPartiesAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, display_index, name, party_type
            from user_party_groups
            where "userId" = $1
            order by display_index
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[] { reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3) });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadPartySlotsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, party_id, slot_number, character_card_id, accessory_id, poster_id, position
            from user_party_slots
            where "userId" = $1
            order by party_id, slot_number
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                GetNullableInt64(reader, 3),
                GetNullableInt64(reader, 4),
                GetNullableInt64(reader, 5),
                reader.GetInt32(6)
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadMissionsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, is_cleared, is_received, progress, completed_at, mission_from, mission_to
            from user_mission_statuses
            where "userId" = $1
            order by mission_from, mission_to
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetInt32(3),
                GetNullableDateTime(reader, 4),
                reader.GetInt32(5),
                reader.GetInt32(6)
            });
        }
        return result;
    }

    static async Task<object?[]> ReadLiveUserStatsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select score_rating, technical_rating
            from user_live_user_stats
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new object?[] { userId, 0.0, 0.0 };
        return new object?[] { userId, reader.GetDouble(0), reader.GetDouble(1) };
    }

    static async Task<List<object?[]>> ReadMusicBookmarksAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select music_master_id, bookmark_state
            from user_music_bookmarks
            where "userId" = $1
            order by music_master_id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                reader.GetInt32(1)
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadGameHintReadsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, category, is_read
            from user_game_hint_reads
            where "userId" = $1
            order by category
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetBoolean(2)
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadShopLastViewsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, shop_master_id, last_viewed_at, shop_category
            from user_shop_last_views
            where "userId" = $1
            order by shop_category, shop_master_id nulls first
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                GetNullableInt64(reader, 1),
                reader.GetDateTime(2),
                reader.GetInt32(3)
            });
        }
        return result;
    }

    static async Task<List<object?[]>> ReadEpisodeReadStatesAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select episode_master_id, is_new
            from user_episode_read_states
            where "userId" = $1
            order by episode_master_id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                reader.GetBoolean(1)
            });
        }
        return result;
    }

    static async Task<object?[]> ReadPlayerSettingAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, display_quality, download_category, is_push_enabled, push_start_at, push_end_at, is_live_push_enabled
            from user_player_settings
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return BuildInitialPlayerSetting(userId);
        return new object?[]
        {
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetBoolean(3),
            GetNullableDateTime(reader, 4),
            GetNullableDateTime(reader, 5),
            reader.GetBoolean(6),
            null,
            null,
            null,
            null
        };
    }

    static async Task<object?[]> ReadRestrictionAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select restricted_until, reason
            from user_restriction_states
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new object?[] { userId, null, true };
        return new object?[] { userId, GetNullableDateTime(reader, 0), true };
    }

    static async Task<List<object?[]>> ReadAlbumsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select album_index, name, display_order
            from user_album_groups
            where "userId" = $1
            order by display_order
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new object?[] { reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2) });
        }
        return result;
    }

    static async Task<object?[]> ReadInviteStateAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select invited_count, invite_code, is_reward_received
            from user_invite_states
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return BuildInviteCode(userId);
        return new object?[] { reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2) };
    }

    async Task<long?> FindMostRecentUserIdAsync()
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select user_accounts.id
            from user_login_identities
            join user_accounts on user_accounts.id = user_login_identities."userId"
            order by user_login_identities.last_seen_at desc, user_login_identities.first_seen_at desc
            limit 1
            """,
            connection);
        var value = await command.ExecuteScalarAsync();
        return value is long userId ? userId : null;
    }

    async Task<long?> FindLatestUserIdAsync()
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select id
            from user_accounts
            order by id desc
            limit 1
            """,
            connection);
        var value = await command.ExecuteScalarAsync();
        return value is long userId ? userId : null;
    }

    async Task<long?> GetCurrentUserIdAsync(HttpContext context)
    {
        return TryReadUserIdFromRequest(context) ?? await FindMostRecentUserIdAsync();
    }

    static async Task<string?> ReadUserNameAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        await using var command = new NpgsqlCommand("select name from user_accounts where id = $1", connection, transaction);
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteScalarAsync() as string;
    }

    async Task<string> ReadPublicIdAsync(long userId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("select public_id from user_accounts where id = $1", connection);
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteScalarAsync() as string ?? FormatNumericId(userId);
    }

    static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values)
        {
            command.Parameters.AddWithValue(value);
        }
        await command.ExecuteNonQueryAsync();
    }

    static long? TryReadUserIdFromRequest(HttpContext context)
    {
        var token = ReadToken(context);
        if (string.IsNullOrWhiteSpace(token)) return null;

        var parts = token.Split('.');
        if (parts.Length != 2 && parts.Length != 3) return null;
        var payloadPart = parts.Length == 3 ? parts[1] : parts[0];

        try
        {
            using var doc = JsonDocument.Parse(Base64UrlDecode(payloadPart));
            if (doc.RootElement.TryGetProperty("uid", out var uid) && uid.TryGetInt64(out var userId))
            {
                return userId;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    static string? ReadToken(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization["Bearer ".Length..].Trim();
        }

        foreach (var header in new[] { "X-Api-Token", "Api-Token", "ApiToken" })
        {
            var value = context.Request.Headers[header].ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return null;
    }

    static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    static int? ExtractFirstInt(object? value)
    {
        return value switch
        {
            null => null,
            int number => number,
            long number => checked((int)number),
            short number => number,
            byte number => number,
            object?[] array => array.Select(ExtractFirstInt).FirstOrDefault(number => number is not null),
            IEnumerable<object?> values => values.Select(ExtractFirstInt).FirstOrDefault(number => number is not null),
            _ => null
        };
    }

    static int? ReadIntQuery(HttpContext context, string name)
    {
        var value = context.Request.Query[name].FirstOrDefault();
        return int.TryParse(value, out var result) ? result : null;
    }

    static int? GetInt(object?[] values, int index)
    {
        if (index >= values.Length) return null;
        return values[index] switch
        {
            int value => value,
            long value => checked((int)value),
            short value => value,
            byte value => value,
            string text when int.TryParse(text, out var value) => value,
            _ => null
        };
    }

    static DateTime? ExtractFirstDateTime(object? value)
    {
        return value switch
        {
            null => null,
            DateTime dateTime => dateTime,
            string text when DateTime.TryParse(text, out var parsed) => parsed,
            long seconds when seconds > 0 => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime,
            int seconds when seconds > 0 => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime,
            double seconds when seconds > 0 => DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime,
            object?[] array => array.Select(ExtractFirstDateTime).FirstOrDefault(dateTime => dateTime is not null),
            IEnumerable<object?> values => values.Select(ExtractFirstDateTime).FirstOrDefault(dateTime => dateTime is not null),
            _ => null
        };
    }

    static int NormalizeTutorialStatus(int tutorialStatus)
    {
        return tutorialStatus is >= 0 and <= 6 or 99 ? tutorialStatus : 99;
    }

    static long? GetNullableInt64(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    static double? GetNullableDouble(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    static DateTime? GetNullableDateTime(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    static string? GetString(object?[] values, int index)
    {
        return index < values.Length ? values[index] as string : null;
    }

    static long? GetLong(object?[] values, int index)
    {
        if (index >= values.Length) return null;
        return values[index] switch
        {
            long value => value,
            int value => value,
            uint value => value,
            short value => value,
            ushort value => value,
            byte value => value,
            sbyte value => value,
            string text when long.TryParse(text, out var value) => value,
            _ => null
        };
    }

    static long? GetNullableLong(object?[] values, int index)
    {
        return index < values.Length && values[index] is null ? null : GetLong(values, index);
    }

    static bool? GetBool(object?[] values, int index)
    {
        if (index >= values.Length) return null;
        return values[index] switch
        {
            bool value => value,
            byte value => value != 0,
            int value => value != 0,
            long value => value != 0,
            _ => null
        };
    }

    static long UserScopedId(long userId, long localId)
    {
        return userId * 1000000 + localId;
    }

    static string FormatNumericId(long userId)
    {
        return (1000000000L + (userId % 9000000000L)).ToString("D10");
    }

    static object?[] DataObject(int unionKey, object?[] value)
    {
        return new object?[] { unionKey, value };
    }

    static object?[] BuildLoginMissionRefreshPresentData()
    {
        return new object?[]
        {
            DataObject(48, new object?[] { 126984507L, false, false, 0, null, 100000, 100001 }),
            DataObject(48, new object?[] { 126984614L, false, false, 0, null, 100200, 100201 }),
            DataObject(48, new object?[] { 126984536L, false, false, 0, null, 100300, 100301 }),
            DataObject(48, new object?[] { 126984983L, false, false, 0, null, 100400, 100401 })
        };
    }

    sealed record GachaHistoryRow(
        long GachaDetailMasterId,
        int ThingType,
        long ThingMasterId,
        int Quantity,
        object?[] Payload);

    public sealed record LoginBonusReceiveResult(object?[] Bonuses, object?[] PresentData);

    public sealed record InboxCheckPackagesResult(bool IsSuccess, object?[] PresentData);

    public sealed record InboxReceivePackagesResult(object?[] Rewards, bool HasUnread, object?[] PresentData);

    sealed record InboxPackageRecord(
        long Id,
        int ThingType,
        long MasterId,
        int Quantity,
        string Message,
        DateTime CreatedAt,
        DateTime? ExpiresAt);

    sealed record LoginBonusReward(
        long Id,
        string Title,
        int ThingType,
        long MasterId,
        int Quantity,
        int Days,
        bool IsLoop,
        int DisplayOrder,
        int DisplayCategory,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt);
}


