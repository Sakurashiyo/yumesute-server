static partial class ApiEndpointMappings
{
    static void MapCommerceMediaCircleEndpoints(WebApplication app, LocalServerState state, ApiCodec codec, LocalRequestLogger logger)
    {
        MapPermanentMarketEndpoint(app,state,codec,logger);
        app.MapMethods("/api/SerialCodes/UseSerialCode", new[] { "GET", "POST" }, async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"serial-codes-use payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, new object?[] { 3 });
        });

        app.MapMethods("/api/Photo/GetAlbumMainPage", new[] { "GET", "POST" }, async context =>
        {
            var targetUserId = context.Request.Query["targetUserId"].FirstOrDefault();
            await logger.LogAsync($"photo-get-album-main-page targetUserId={targetUserId}");
            await codec.WriteApiResultAsync(context, new object?[] { null, false });
        });

        app.MapPost("/api/Photo/AlbumSimpleArranging", async context =>
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

        app.MapPost("/api/Photo/AlbumDetailArranging", async context =>
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

        app.MapPost("/api/Photos/GeneratePhoto", async context =>
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

        app.MapMethods("/api/Photos/FinishGeneratePhoto", new[] { "GET", "POST" }, async context =>
        {
            var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null;
            await logger.LogAsync($"photos-finish-generate-photo payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, new object?[] { true }, "five-frame");
        });

        app.MapMethods("/api/Photo/WatchMusicVideo", new[] { "GET", "POST" }, async context =>
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

        app.MapMethods("/api/Posters/{posterId:long}/LevelUp/{levelTo:int}", new[] { "GET", "POST" }, async (HttpContext context, long posterId, int levelTo) =>
        {
            try
            {
                if (context.Request.Query.ContainsKey("uPosterId") &&
                    (!long.TryParse(context.Request.Query["uPosterId"], out var queryId) || queryId != posterId) ||
                    context.Request.Query.ContainsKey("levelTo") &&
                    (!int.TryParse(context.Request.Query["levelTo"], out var queryLevel) || queryLevel != levelTo))
                    throw new BadHttpRequestException(PosterProgressionErrors.Invalid, 400);
                var present = await state.UserDataService.LevelUpPosterAsync(context, posterId, levelTo);
                await codec.WriteApiFramesAsync(context, new object?[] { true }, present,
                    Array.Empty<object?>(), Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
            }
            catch (BadHttpRequestException error) when (error.Message is PosterProgressionErrors.Invalid
                or PosterProgressionErrors.NotOwned or PosterProgressionErrors.LevelLimit or PosterProgressionErrors.InsufficientItems)
            {
                await logger.LogAsync($"level=WARN operation=poster-level-up posterId={posterId} requestId={context.TraceIdentifier} errorCode={error.Message} outcome=failed");
                await codec.WriteApiErrorAsync(context, error.Message, error.StatusCode);
            }
        });

        app.MapMethods("/api/Shops/ViewPage", new[] { "GET", "POST" }, async context =>
        {
            var present = await state.UserDataService.GetShopPurchasePresentAsync(context);
            await codec.WriteApiFramesAsync(context, new object?[] { Array.Empty<object?>() }, present,
                Array.Empty<object?>(), Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/api/Shops/Purchase", async context =>
        {
            try
            {
                var body = await codec.ReadRequestBodyAsync(context);
                var purchase = await state.UserDataService.PurchaseShopItemAsync(context, body);
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

        app.MapMethods("/api/Shops/UpdateLastViewedAt", new[] { "GET", "POST" }, async context =>
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

        app.MapMethods("/api/Shops/GetOrRefreshMarket", new[] { "GET", "POST" }, async context =>
        {
            try { var response = await state.UserDataService.GetMarketAsync(context, false); await codec.WriteApiFramesAsync(context, response.Result, response.Present, Array.Empty<object?>(), Array.Empty<object?>(), "shops-get-or-refresh-market-five-frame"); }
            catch (BadHttpRequestException error) { context.Response.StatusCode=error.StatusCode; await codec.WriteApiFramesAsync(context, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), "five-frame"); }
        });

        app.MapMethods("/api/Shops/RefreshMarketWithJewel", new[] { "GET", "POST" }, async context =>
        {
            try { var response = await state.UserDataService.GetMarketAsync(context, true); await codec.WriteApiFramesAsync(context, response.Result, response.Present, Array.Empty<object?>(), Array.Empty<object?>(), "shops-refresh-market-with-jewel-five-frame"); }
            catch (BadHttpRequestException error) { context.Response.StatusCode=error.StatusCode; await codec.WriteApiFramesAsync(context, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), "five-frame"); }
        });

        app.MapMethods("/api/Shops/UpdateMarketWithJewel", new[] { "GET", "POST" }, async context =>
        {
            try { var response = await state.UserDataService.GetMarketAsync(context, true); await codec.WriteApiFramesAsync(context, response.Result, response.Present, Array.Empty<object?>(), Array.Empty<object?>(), "shops-refresh-market-with-jewel-five-frame"); }
            catch (BadHttpRequestException error) { context.Response.StatusCode=error.StatusCode; await codec.WriteApiFramesAsync(context, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), "five-frame"); }
        });

        app.MapMethods("/api/Shops/ExchangeMarketThings", new[] { "GET", "POST" }, async context =>
        {
            try { var body = context.Request.Method == "POST" ? await codec.ReadRequestBodyAsync(context) : null; var response = await state.UserDataService.ExchangeMarketThingsAsync(context, body); await codec.WriteApiFramesAsync(context, response.Result, response.Present, Array.Empty<object?>(), Array.Empty<object?>(), "shops-exchange-market-things-five-frame"); }
            catch (BadHttpRequestException error) { context.Response.StatusCode=error.StatusCode; await codec.WriteApiFramesAsync(context, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(), "five-frame"); }
        });

        MapMusicExchangeEndpoints(app,state,codec,logger);

        MapGachaEndpoints(app,state,codec,logger);
    }
}
