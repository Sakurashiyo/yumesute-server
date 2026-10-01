static partial class ApiEndpointMappings
{
    static void MapCommerceMediaCircleEndpoints(WebApplication app, LocalServerState state, ApiCodec codec, LocalRequestLogger logger)
    {
        MapPermanentMarketEndpoint(app,state,codec,logger);
        app.MapMethods("/localap/api/SerialCodes/UseSerialCode", new[] { "GET", "POST" }, async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"serial-codes-use payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, new object?[] { 3 });
        });

        app.MapMethods("/localap/api/Photo/GetAlbumMainPage", new[] { "GET", "POST" }, async context =>
        {
            var targetUserId = context.Request.Query["targetUserId"].FirstOrDefault();
            await logger.LogAsync($"photo-get-album-main-page targetUserId={targetUserId}");
            await codec.WriteApiResultAsync(context, new object?[] { null, false });
        });

        app.MapPost("/localap/api/Photo/AlbumSimpleArranging", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"photo-album-simple-arranging payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                await state.UserDataService.ArrangePhotoAlbumAsync(context, body, arrangementType: 1),
                Array.Empty<object?>(),
                new object?[] { new object?[] { 0, new object?[] { 11, 11 } } },
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Photo/AlbumDetailArranging", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"photo-album-detail-arranging payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                await state.UserDataService.ArrangePhotoAlbumAsync(context, body, arrangementType: 2),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Photos/GeneratePhoto", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var itemMasterId = ExtractFirstLong(body) ?? 510005L;
            var photoId = Random.Shared.Next(39000000, 39999999);
            var fileName = $"{Guid.NewGuid():D}.jpg";
            var sasToken = GameResults.BuildPhotoSasToken("photo");
            await logger.LogAsync($"photos-generate-photo photoId={photoId} itemMasterId={itemMasterId} fileName={fileName}");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.GeneratePhotoResult(photoId, fileName, sasToken),
                GameResults.GeneratePhotoPresentData(photoId, fileName, sasToken, itemMasterId),
                Array.Empty<object?>(),
                GameResults.GeneratePhotoNotifications(),
                "result-present-lz4-five-frame");
        });

        app.MapMethods("/localap/api/Photos/FinishGeneratePhoto", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"photos-finish-generate-photo payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, new object?[] { true }, "five-frame");
        });

        app.MapMethods("/localap/api/Photo/WatchMusicVideo", new[] { "GET", "POST" }, async context =>
        {
            var musicVideoId = ReadLongQuery(context, "mMusicVideoId") ?? 0L;
            await logger.LogAsync($"photo-watch-music-video musicVideoId={musicVideoId}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                new object?[] { new object?[] { 131, new object?[] { Random.Shared.Next(100000, 999999), musicVideoId } } },
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "photo-watch-music-video-five-frame");
        });

        app.MapMethods("/localap/api/Posters/{posterId:long}/LevelUp/{levelTo:int}", new[] { "GET", "POST" }, async (HttpContext context, long posterId, int levelTo) =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"posters-level-up posterId={posterId} levelTo={levelTo} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                GameResults.PosterLevelUpPresentData(posterId, levelTo),
                Array.Empty<object?>(),
                new object?[] { new object?[] { 0, new object?[] { 15, 15 } } },
                "posters-level-up-five-frame");
        });

        app.MapMethods("/localap/api/Shops/ViewPage", new[] { "GET", "POST" }, async context =>
        {
            var present = await state.UserDataService.GetShopPurchasePresentAsync(context);
            await codec.WriteApiFramesAsync(context, new object?[] { Array.Empty<object?>() }, present,
                Array.Empty<object?>(), Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Shops/Purchase", async context =>
        {
            try
            {
                var body = await codec.ReadRequestBodyAsync(context);
                var purchase = await state.UserDataService.PurchaseDailyFreePackAsync(context, body);
                await codec.WriteApiFramesAsync(context, purchase.Rewards, purchase.Present, Array.Empty<object?>(),
                    purchase.Rewards.Length > 0 ? GameResults.ShopPurchaseNotifications() : Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
            }
            catch (BadHttpRequestException error)
            {
                await logger.LogAsync($"level=WARN operation=shop-purchase errorCode={error.Message} outcome=failed");
                context.Response.StatusCode = error.StatusCode;
                await codec.WriteApiFramesAsync(context, Array.Empty<object?>(), Array.Empty<object?>(),
                    Array.Empty<object?>(), Array.Empty<object?>(), "five-frame");
            }
        });

        app.MapMethods("/localap/api/Shops/UpdateLastViewedAt", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"shops-update-last-viewed-at payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                await state.UserDataService.UpdateShopLastViewedAtAsync(context, body),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "shops-update-last-viewed-at-five-frame");
        });

        app.MapMethods("/localap/api/Shops/GetOrRefreshMarket", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"shops-get-or-refresh-market payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.MarketResult(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "shops-get-or-refresh-market-five-frame");
        });

        app.MapMethods("/localap/api/Shops/RefreshMarketWithJewel", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"shops-refresh-market-with-jewel payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.MarketResult(),
                GameResults.RefreshMarketPresentData(withJewel: true),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "shops-refresh-market-with-jewel-five-frame");
        });

        app.MapMethods("/localap/api/Shops/ExchangeMarketThings", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"shops-exchange-market-things payload={ValueFormatter.Format(body)}");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.ExchangeMarketThingsResult(),
                GameResults.ExchangeMarketThingsPresentData(),
                Array.Empty<object?>(),
                new object?[] { new object?[] { 0, new object?[] { 9, 9 } } },
                "shops-exchange-market-things-five-frame");
        });

        app.MapMethods("/localap/api/Shops/ExchangeMusic/{musicMasterId:long}", new[] { "GET", "POST" }, async (HttpContext context, long musicMasterId) =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"shops-exchange-music musicMasterId={musicMasterId} payload={ValueFormatter.Format(body)}");
            var presentData = GameResults.ExchangeMusicPresentData(musicMasterId);
            await codec.WriteApiFramesAsync(
                context,
                GameResults.ExchangeMusicResult(musicMasterId),
                presentData,
                Array.Empty<object?>(),
                GameResults.ExchangeMusicNotifications(musicMasterId),
                presentData.Length > 3
                    ? "present-lz4-when-not-empty-five-frame"
                    : "shops-exchange-music-five-frame");
        });

        MapGachaEndpoints(app,state,codec,logger);
    }
}
