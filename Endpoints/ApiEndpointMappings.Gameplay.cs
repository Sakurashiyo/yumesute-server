static partial class ApiEndpointMappings
{
    static void MapGameplayEndpoints(WebApplication app, LocalServerState state, ApiCodec codec, LocalRequestLogger logger)
    {
        MapLessonEndpoints(app, state, codec, logger);
        app.MapMethods("/localap/api/Lives/UpdateClearLamps/{multiLiveId:long}", new[] { "GET", "POST" }, async (HttpContext context, long multiLiveId) =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"lives-update-clear-lamps multiLiveId={multiLiveId} query={context.Request.QueryString} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, new object?[] { 0 });
        });

        app.MapMethods("/localap/api/Lives/UpdateClearLamp/{multiLiveId:long}", new[] { "GET", "POST" }, async (HttpContext context, long multiLiveId) =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"lives-update-clear-lamp multiLiveId={multiLiveId} query={context.Request.QueryString} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, new object?[] { 0 });
        });

        app.MapGet("/localap/api/Lives/GetMultiLiveInformation/{multiLiveId:long}", async (HttpContext context, long multiLiveId) =>
        {
            await logger.LogAsync($"lives-get-multi-live-information multiLiveId={multiLiveId} query={context.Request.QueryString}");
            var realtimeInformation = state.MultiLiveRealtime.GetMultiLiveInformation(multiLiveId);
            var result = realtimeInformation.Length == 0
                ? GameResults.MultiLiveInformation(multiLiveId)
                : realtimeInformation;
            await codec.WriteApiFramesAsync(
                context,
                result,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                GameResults.IsCompressedMultiLiveInformation(multiLiveId) ? "result-lz4-five-frame" : "five-frame");
        });

        app.MapPost("/localap/api/Lives/MatchingGhostLive", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"lives-matching-ghost-live payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.MatchingGhostLiveResult(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-lz4-five-frame");
        });

        app.MapPost("/localap/api/Lives/Start", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"lives-start payload={ValueFormatter.Format(body)}");
            await state.UserDataService.RegisterLiveStartAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                GameResults.LiveStartResult(body),
                await state.UserDataService.GetMusicPresentDataAsync(context),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-lz4-five-frame");
        });

        app.MapPost("/localap/api/Lives/StartGhostLive", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"lives-start-ghost-live payload={ValueFormatter.Format(body)}");
            await state.UserDataService.RegisterLiveStartAsync(context, body, validateMusicUnlock:false);
            await codec.WriteApiFramesAsync(
                context,
                GameResults.GhostLiveStartResult(body),
                await state.UserDataService.GetMusicPresentDataAsync(context),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-lz4-five-frame");
        });

        app.MapPost("/localap/api/Lives/StartMultiLive", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"lives-start-multi-live payload={ValueFormatter.Format(body)}");
            if (body is object?[] multi && multi.Length >= 3)
                await state.UserDataService.RegisterLiveStartAsync(context, new object?[] { null, multi[0], null, null, multi[2], 1, null, false }, validateMusicUnlock:false);
            await codec.WriteApiFramesAsync(
                context,
                GameResults.MultiLiveStartResult(body),
                await state.UserDataService.GetMusicPresentDataAsync(context),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-lz4-five-frame");
        });

        app.MapMethods("/localap/api/Lives/StartMusicCourseLive", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"lives-start-music-course-live payload={ValueFormatter.Format(body)}");
            await state.UserDataService.RegisterLiveStartAsync(context, body, validateMusicUnlock:false);
            await codec.WriteApiFramesAsync(
                context,
                GameResults.LiveStartResult(body),
                await state.UserDataService.GetMusicPresentDataAsync(context),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-lz4-five-frame");
        });

        app.MapPost("/localap/api/Lives/StartTripleCastLive", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"lives-start-triple-cast-live payload={ValueFormatter.Format(body)}");
            if (body is object?[] triple && triple.Length >= 1)
                await state.UserDataService.RegisterLiveStartAsync(context, new object?[] { null, triple[0], null, null, true, 1, null, false }, validateMusicUnlock:false);
            await codec.WriteApiFramesAsync(
                context,
                GameResults.TripleCastLiveStartResult(body),
                (await state.UserDataService.GetMusicPresentDataAsync(context)).Concat(
                    GameResults.TripleCastLiveStartPresentData(body).OfType<object?[]>().Where(row=>row[0] is not 25)).ToArray(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-present-lz4-five-frame");
        });

        app.MapPost("/localap/api/Lives/Music/EditBookmark", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"lives-music-edit-bookmark payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                await state.UserDataService.EditMusicBookmarkAsync(context, body),
                Array.Empty<object?>(),
                Array.Empty<object?>());
        });

        app.MapMethods("/localap/api/Lives/StartTrialPartyEventStage", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"lives-start-trial-party-event-stage payload={ValueFormatter.Format(body)}");
            await state.UserDataService.RegisterLiveStartAsync(context, body, validateMusicUnlock:false);
            await codec.WriteApiFramesAsync(
                context,
                GameResults.LiveStartResult(body),
                await state.UserDataService.GetMusicPresentDataAsync(context),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-lz4-five-frame");
        });

        app.MapPost("/localap/api/Lives/StartLesson", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var lesson = await state.UserDataService.StartLessonLiveAsync(context, body);
            await logger.LogAsync("lives-start-lesson outcome=success");
            await codec.WriteApiFramesAsync(
                context,
                lesson.Result,
                lesson.Present,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-lz4-five-frame");
        });

        app.MapMethods("/localap/api/Lives/Retire", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"lives-retire payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "lives-retire-five-frame");
        });

        app.MapPost("/localap/api/Lives/FinishAndValidate", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"lives-finish-and-validate payload={ValueFormatter.Format(body)}");
            var settlement = await state.UserDataService.BuildLiveSettlementAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                settlement.Result,
                settlement.Present,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-present-lz4-five-frame");
        });

        app.MapPost("/localap/api/Lives/FinishAnotherNotationLive", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var settlement = await state.UserDataService.FinishAnotherNotationAsync(context, body);
            await codec.WriteApiFramesAsync(context, settlement.Result, settlement.Present,
                Array.Empty<object?>(), Array.Empty<object?>(), "result-present-lz4-five-frame");
        });

        app.MapPost("/localap/api/Items/UseStaminaRecoveryItems", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"items-use-stamina-recovery-items payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                await state.UserDataService.UseStaminaRecoveryItemsAsync(context, body),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/CharacterBases/{characterBaseMasterId:long}/SetCostume/{costumeMasterId:long}", async (HttpContext context, long characterBaseMasterId, long costumeMasterId) =>
        {
            await logger.LogAsync($"character-bases-set-costume characterBaseMasterId={characterBaseMasterId} costumeMasterId={costumeMasterId}");
            var present = await state.UserDataService.SetCostumeAsync(context, characterBaseMasterId, costumeMasterId);
            await codec.WriteApiFramesAsync(context, new object?[] { true }, present, Array.Empty<object?>(), Array.Empty<object?>());
        });

        app.MapPost("/localap/api/Characters/SetPortalMCharacter", async (HttpContext context) =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var present = await state.UserDataService.SetPortalCharacterAsync(context, body);
            await codec.WriteApiFramesAsync(context, new object?[] { true }, present, Array.Empty<object?>(), Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Characters/{characterId:long}/AddExperience", async (HttpContext context, long characterId) =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"characters-add-experience characterId={characterId} payload={ValueFormatter.Format(body)}");
            var present = await state.UserDataService.AddCharacterExperienceAsync(context, characterId, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                present,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "characters-add-experience-present-lz4-five-frame");
        });

        app.MapPost("/localap/api/Characters/{characterId:long}/EnhanceSenseLevel/{senseLevel:int}", async (HttpContext context, long characterId, int senseLevel) =>
        {
            var priority = 1;
            if (context.Request.Query.ContainsKey("priority") && !int.TryParse(context.Request.Query["priority"], out priority))
                throw new BadHttpRequestException(CharacterSenseErrors.InvalidRequest);
            var present = await state.UserDataService.EnhanceCharacterSenseAsync(context, characterId, senseLevel, priority);
            await codec.WriteApiFramesAsync(context, new object?[] { true }, present,
                Array.Empty<object?>(), Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Characters/{characterMasterId:long}/ReleaseSideStory", async (HttpContext context, long characterMasterId) =>
        {
            var order = ReadIntQuery(context, "order") ?? 1;
            await logger.LogAsync($"characters-release-side-story characterMasterId={characterMasterId} order={order} query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                await state.UserDataService.ReleaseSideStoryAsync(context, characterMasterId, order),
                Array.Empty<object?>(),
                Array.Empty<object?>());
        });

        app.MapPost("/localap/api/Characters/BulkLevelUp", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"characters-bulk-level-up payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                GameResults.CharacterBulkLevelUpPresentData(body),
                Array.Empty<object?>(),
                GameResults.CharacterBulkLevelUpNotifications(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/CharacterMissions/{characterBaseMasterId:long}/receiveAllMission", async (HttpContext context, long characterBaseMasterId) =>
        {
            await logger.LogAsync($"character-missions-receive-all characterBaseMasterId={characterBaseMasterId} query={context.Request.QueryString}");
            var received = await state.UserDataService.ReceiveCharacterMissionsAsync(context, new[] { characterBaseMasterId }, false);
            await codec.WriteApiFramesAsync(context,received.Result,received.Present,Array.Empty<object?>(),Array.Empty<object?>(),"present-lz4-when-not-empty-five-frame");
        });

        app.MapMethods("/localap/api/CharacterMissions/BulkReceiveAllMission", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"character-missions-bulk-receive-all payload={ValueFormatter.Format(body)}");
            if (body is not object?[] ids || ids.Length is < 1 or > 100 || ids.Any(id => id is not (byte or sbyte or short or ushort or int or uint or long)))
                throw new BadHttpRequestException(CharacterMissionErrors.InvalidRequest);
            var received = await state.UserDataService.ReceiveCharacterMissionsAsync(context, ids.Select(Convert.ToInt64).ToArray(), true);
            await codec.WriteApiFramesAsync(context,received.Result,received.Present,Array.Empty<object?>(),Array.Empty<object?>(),"present-lz4-when-not-empty-five-frame");
        });
        app.MapPost("/localap/api/CharacterMissions/{characterBaseMasterId:long}/receiveKeyMission", async (HttpContext context,long characterBaseMasterId) =>
        {
            var received = await state.UserDataService.ReceiveCharacterMissionsAsync(context,new[] {characterBaseMasterId},false,true);
            await codec.WriteApiFramesAsync(context,received.Result,received.Present,Array.Empty<object?>(),Array.Empty<object?>(),"present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Comics/Read/{comicEpisodeId:long}", async (HttpContext context, long comicEpisodeId) =>
        {
            await logger.LogAsync($"comics-read comicEpisodeId={comicEpisodeId} query={context.Request.QueryString}");
            await codec.WriteApiResultAsync(context, GameResults.ComicReadRewardResult());
        });

        app.MapPost("/localap/api/Missions/receiveRewards", async context =>
        {
            var missionCategory = ReadIntQuery(context, "missionCategory");
            await logger.LogAsync($"missions-receive-rewards missionCategory={missionCategory} query={context.Request.QueryString}");
            var claim = await state.UserDataService.ReceiveMissionRewardsAsync(context, null, missionCategory);
            await codec.WriteApiFramesAsync(
                context,
                claim.Rewards,
                claim.Present,
                Array.Empty<object?>(),
                Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Missions/{missionId:long}/receiveCurrentRewards", async (HttpContext context, long missionId) =>
        {
            await logger.LogAsync($"missions-receive-current-rewards missionId={missionId} query={context.Request.QueryString}");
            var claim = await state.UserDataService.ReceiveMissionRewardsAsync(context, missionId, null);
            await codec.WriteApiFramesAsync(
                context,
                claim.Rewards,
                claim.Present,
                Array.Empty<object?>(),
                Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Missions/MissionPassReceiveRewards/{missionPassId:long}", async (HttpContext context, long missionPassId) =>
        {
            await logger.LogAsync($"missions-pass-receive-rewards missionPassId={missionPassId} query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.MissionPassReceiveRewardsResult(missionPassId),
                GameResults.MissionPassReceiveRewardsPresentData(missionPassId),
                Array.Empty<object?>(),
                Array.Empty<object?>());
        });

        app.MapGet("/localap/api/MultiRooms", async context =>
        {
            await logger.LogAsync($"multi-rooms query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.MultiRoomsList(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-lz4-five-frame");
        });

        app.MapPost("/localap/api/MultiRooms/Detail", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"multi-rooms-detail payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.MultiRoomDetail(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-lz4-five-frame");
        });

        app.MapPost("/localap/api/MultiRooms/Joined", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"multi-rooms-joined payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>());
        });

        app.MapPost("/localap/api/MultiRooms/Invited", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"multi-rooms-invited payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>());
        });

        app.MapPost("/localap/api/Parties/{partyId:long}/EditParty", async (HttpContext context, long partyId) =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var present = await state.UserDataService.EditPartyAsync(context, partyId, body);
            await logger.LogAsync($"parties-edit-party saved partyId={partyId} members=5");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                present,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Parties/{partyId:long}/EditPartyWithRecommended", async (HttpContext context, long partyId) =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var present = await state.UserDataService.EditPartyAsync(context, partyId, body);
            await logger.LogAsync($"parties-edit-party-with-recommended saved partyId={partyId} members=5");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                present,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });
    }
}
