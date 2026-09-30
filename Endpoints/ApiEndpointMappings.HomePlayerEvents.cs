static partial class ApiEndpointMappings
{
    static void MapHomePlayerEventEndpoints(WebApplication app, LocalServerState state, ApiCodec codec, LocalRequestLogger logger)
    {
        app.MapMethods("/localap/api/Home/CheckReceiveLoginBonus", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("home-check-receive-login-bonus");
            var result = await state.UserDataService.CheckReceiveLoginBonusAsync(context);
            await codec.WriteApiFramesAsync(context, result.Bonuses, result.PresentData, Array.Empty<object?>(), Array.Empty<object?>(), "result-present-lz4-when-not-empty-five-frame");
        });

        app.MapMethods("/localap/api/Home/CheckReceiveLoginBonusAsync", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("home-check-receive-login-bonus-async");
            var result = await state.UserDataService.CheckReceiveLoginBonusAsync(context);
            await codec.WriteApiFramesAsync(context, result.Bonuses, result.PresentData, Array.Empty<object?>(), Array.Empty<object?>(), "result-present-lz4-when-not-empty-five-frame");
        });

        app.MapMethods("/localap/api/Home/CheckEexternalPayment", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("home-check-external-payment");
            await codec.WriteApiResultAsync(context, new object?[] { Array.Empty<object?>() }, "five-frame");
        });

        app.MapMethods("/localap/api/Home/GetMultiLiveRestrictionNotification", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("home-get-multilive-restriction-notification");
            await codec.WriteApiResultAsync(context, new object?[] { true });
        });

        app.MapMethods("/localap/api/Home/GetMultiLiveRestrictionNotificationAsync", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("home-get-multilive-restriction-notification-async");
            await codec.WriteApiResultAsync(context, new object?[] { true });
        });

        app.MapMethods("/localap/api/Home/GetNotifications", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("home-get-notifications");
            await WriteSafeNotificationsAsync(context, state, codec, logger);
        });

        app.MapMethods("/localap/api/Home/GetNotificationsAsync", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("home-get-notifications-async");
            await WriteSafeNotificationsAsync(context, state, codec, logger);
        });

        app.MapMethods("/localap/api/Home/GetNotificationsInTitleAsync", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("home-get-notifications-in-title-async");
            await WriteSafeNotificationsAsync(context, state, codec, logger);
        });

        app.MapMethods("/localap/api/Home/GetNotificationsAnonymous", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("home-get-notifications-anonymous");
            await WriteSafeNotificationsAsync(context, state, codec, logger);
        });

        app.MapMethods("/localap/api/Home/GetNotificationsAnonymousAsync", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("home-get-notifications-anonymous-async");
            await WriteSafeNotificationsAsync(context, state, codec, logger);
        });

        app.MapMethods("/localap/api/Home/GetNotificationContent", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            var id = ReadNotificationId(context, body);
            await logger.LogAsync($"home-get-notification-content id={id} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetNotificationContentAsync(id));
        });

        app.MapMethods("/localap/api/Home/GetNotificationContentAsync", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            var id = ReadNotificationId(context, body);
            await logger.LogAsync($"home-get-notification-content-async id={id} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetNotificationContentAsync(id));
        });

        app.MapMethods("/localap/api/Home/GetNotificationContentAnonymous", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            var id = ReadNotificationId(context, body);
            await logger.LogAsync($"home-get-notification-content-anonymous id={id} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetNotificationContentAsync(id));
        });

        app.MapMethods("/localap/api/Home/GetNotificationContentAnonymousAsync", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            var id = ReadNotificationId(context, body);
            await logger.LogAsync($"home-get-notification-content-anonymous-async id={id} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetNotificationContentAsync(id));
        });

        app.MapMethods("/localap/api/Home/GetNotificationsAsync/{mNotificationId:long}", new[] { "GET", "POST" }, async (HttpContext context, long mNotificationId) =>
        {
            await logger.LogAsync($"home-get-notifications-async-content id={mNotificationId} query={context.Request.QueryString}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetNotificationContentAsync(mNotificationId));
        });

        app.MapMethods("/localap/api/Home/GetNotificationsInTitleAsync/{mNotificationId:long}", new[] { "GET", "POST" }, async (HttpContext context, long mNotificationId) =>
        {
            await logger.LogAsync($"home-get-notifications-in-title-async-content id={mNotificationId} query={context.Request.QueryString}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetNotificationContentAsync(mNotificationId));
        });

        app.MapPost("/localap/api/Home/UpdateNotificationReadTime", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"home-update-notification-read-time payload={ValueFormatter.Format(body)}");
            var presentData = await state.UserDataService.UpdateNotificationReadTimeAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                presentData,
                Array.Empty<object?>(),
                Array.Empty<object?>());
        });

        app.MapPost("/localap/api/Home/UpdateNotificationReadTimeAsync", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"home-update-notification-read-time-async payload={ValueFormatter.Format(body)}");
            var presentData = await state.UserDataService.UpdateNotificationReadTimeAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                presentData,
                Array.Empty<object?>(),
                Array.Empty<object?>());
        });

        app.MapPost("/localap/api/Player/UpdateHomeLastTransitionTime", async context =>
        {
            await logger.LogAsync("player-update-home-last-transition-time");
            await state.UserDataService.UpdateHomeLastTransitionTimeAsync(context);
            await codec.WriteApiResultAsync(context, new object?[] { true });
        });

        app.MapPost("/localap/api/Player/UpdateHomeLastTransitionTimeAsync", async context =>
        {
            await logger.LogAsync("player-update-home-last-transition-time-async");
            await state.UserDataService.UpdateHomeLastTransitionTimeAsync(context);
            await codec.WriteApiResultAsync(context, new object?[] { true });
        });

        app.MapMethods("/localap/api/Player/UpdateHomeDisplayPreference", new[] { "GET", "POST" }, async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var preference = await state.UserDataService.UpdateHomeDisplayPreferenceAsync(context, body);
            await logger.LogAsync($"player-update-home-display-preference payload={ValueFormatter.Format(body)} result={ValueFormatter.Format(preference)}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                new object?[] { new object?[] { 3, preference } },
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "player-update-home-display-preference-five-frame");
        });

        app.MapMethods("/localap/api/Player/UpdateHomeDisplayPreferenceAsync", new[] { "GET", "POST" }, async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var preference = await state.UserDataService.UpdateHomeDisplayPreferenceAsync(context, body);
            await logger.LogAsync($"player-update-home-display-preference-async payload={ValueFormatter.Format(body)} result={ValueFormatter.Format(preference)}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                new object?[] { new object?[] { 3, preference } },
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "player-update-home-display-preference-async-five-frame");
        });

        app.MapPost("/localap/api/Player/UpdateSplashLastDisplayTime", async context =>
        {
            await logger.LogAsync("player-update-splash-last-display-time");
            await state.UserDataService.UpdateSplashLastDisplayTimeAsync(context);
            await codec.WriteApiResultAsync(context, new object?[] { true });
        });

        app.MapPost("/localap/api/Player/UpdateSplashLastDisplayTimeAsync", async context =>
        {
            await logger.LogAsync("player-update-splash-last-display-time-async");
            await state.UserDataService.UpdateSplashLastDisplayTimeAsync(context);
            await codec.WriteApiResultAsync(context, new object?[] { true });
        });

        app.MapPost("/localap/api/Player/UpdateTutorial", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"player-update-tutorial payload={ValueFormatter.Format(body)}");
            var presentData = await state.UserDataService.UpdateTutorialAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                presentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "player-update-tutorial-five-frame");
        });

        app.MapPost("/localap/api/Player/UpdateTutorialAsync", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"player-update-tutorial-async payload={ValueFormatter.Format(body)}");
            var presentData = await state.UserDataService.UpdateTutorialAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                presentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "player-update-tutorial-async-five-frame");
        });

        app.MapPost("/localap/api/Player/UpdateGameHintRead", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"player-update-game-hint-read payload={ValueFormatter.Format(body)}");
            var presentData = await state.UserDataService.UpdateGameHintReadAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                presentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "player-update-game-hint-read-five-frame");
        });

        app.MapPost("/localap/api/Player/UpdateGameHintReadAsync", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"player-update-game-hint-read-async payload={ValueFormatter.Format(body)}");
            var presentData = await state.UserDataService.UpdateGameHintReadAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                presentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "player-update-game-hint-read-async-five-frame");
        });

        app.MapPost("/localap/api/Player/RecoverStaminaByJewel", async context =>
        {
            await logger.LogAsync($"player-recover-stamina-by-jewel times={context.Request.Query["times"].FirstOrDefault()}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                await state.UserDataService.RecoverStaminaByJewelAsync(context),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapMethods("/localap/api/Player/UpdateCapedPlayerRankAnnounce", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("player-update-caped-player-rank-announce");
            await state.UserDataService.UpdateCapedPlayerRankAnnounceAsync(context);
            await codec.WriteApiResultAsync(context, new object?[] { true });
        });

        app.MapMethods("/localap/api/Player/UpdateCapedPlayerRankAnnounceAsync", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("player-update-caped-player-rank-announce-async");
            await state.UserDataService.UpdateCapedPlayerRankAnnounceAsync(context);
            await codec.WriteApiResultAsync(context, new object?[] { true });
        });

        app.MapMethods("/localap/api/Events/GetEventCampMyRanking/{eventMasterId:long}", new[] { "GET", "POST" }, async (HttpContext context, long eventMasterId) =>
        {
            await logger.LogAsync($"events-get-event-camp-my-ranking eventMasterId={eventMasterId} query={context.Request.QueryString}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetStoryEventCampInfoAsync(context));
        });

        app.MapMethods("/localap/api/Events/GetStoryEventMyRanking/{eventMasterId:long}", new[] { "GET", "POST" }, async (HttpContext context, long eventMasterId) =>
        {
            await logger.LogAsync($"events-get-story-event-my-ranking eventMasterId={eventMasterId} query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(context, GameResults.StoryEventMyRanking(eventMasterId), Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), "events-get-story-event-my-ranking-five-frame");
        });

        app.MapMethods("/localap/api/Events/GetStoryEventNearRanking/{eventMasterId:long}", new[] { "GET", "POST" }, async (HttpContext context, long eventMasterId) =>
        {
            await logger.LogAsync($"events-get-story-event-near-ranking eventMasterId={eventMasterId} query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(context, GameResults.StoryEventRankingList(), Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), "events-get-story-event-near-ranking-five-frame");
        });

        app.MapMethods("/localap/api/Events/GetStoryEventTopRanking/{eventMasterId:long}", new[] { "GET", "POST" }, async (HttpContext context, long eventMasterId) =>
        {
            await logger.LogAsync($"events-get-story-event-top-ranking eventMasterId={eventMasterId} query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(context, GameResults.StoryEventRankingList(), Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), "events-get-story-event-top-ranking-five-frame");
        });

        app.MapMethods("/localap/api/Events/GetStoryEventBorderRanking/{eventMasterId:long}", new[] { "GET", "POST" }, async (HttpContext context, long eventMasterId) =>
        {
            await logger.LogAsync($"events-get-story-event-border-ranking eventMasterId={eventMasterId} query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(context, GameResults.StoryEventRankingList(), Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), "events-get-story-event-border-ranking-five-frame");
        });

        app.MapMethods("/localap/api/Events/GetStoryEventFriendRanking", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync($"events-get-story-event-friend-ranking query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(context, GameResults.StoryEventRankingList(), Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), "events-get-story-event-friend-ranking-five-frame");
        });

        app.MapMethods("/localap/api/Events/GetStoryEventCircleRanking", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync($"events-get-story-event-circle-ranking query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(context, GameResults.StoryEventRankingList(), Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), "events-get-story-event-circle-ranking-five-frame");
        });

        app.MapMethods("/localap/api/Events/ReadTips/{eventMasterId:long}", new[] { "GET", "POST" }, async (HttpContext context, long eventMasterId) =>
        {
            await logger.LogAsync($"events-read-tips eventMasterId={eventMasterId} query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(context, new object?[] { true }, GameResults.StoryEventReadTipsPresentData(eventMasterId), Array.Empty<object?>(), Array.Empty<object?>(), "events-read-tips-five-frame");
        });
    }

    static async Task WriteSafeNotificationsAsync(
        HttpContext context,
        LocalServerState state,
        ApiCodec codec,
        LocalRequestLogger logger)
    {
        await codec.WriteApiResultAsync(context, await state.UserDataService.GetNotificationsAsync(context), "lz4-five-frame");
    }
}
