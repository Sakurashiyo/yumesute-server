static partial class ApiEndpointMappings
{
    public static void MapApiEndpoints(this WebApplication app, LocalServerState state)
    {
        var config = state.Config;
        var codec = state.Codec;
        var logger = state.Logger;

        app.MapGet("/health", () => Results.Json(new { ok = true }));

        app.MapMethods("/localap/api/Environment", new[] { "GET", "POST" }, async context =>
        {
            await codec.WriteApiResultAsync(context, GameResults.Environment(config), "lz4-five-frame");
        });
        app.MapMethods("/localap/api/Environment/GetEnvironment", new[] { "GET", "POST" }, async context =>
        {
            await codec.WriteApiResultAsync(context, GameResults.Environment(config), "lz4-five-frame");
        });
        app.MapMethods("/localap/api/Environment/Ping", new[] { "GET", "POST" }, async context =>
        {
            await codec.WriteApiResultAsync(context, new object[] { true });
        });

        app.MapPost("/localap/api/Account/Register", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var name = PayloadReaders.ReadRegisterName(body) ?? $"Player-{Random.Shared.Next(100000, 999999)}";
            var result = await state.AccountService.RegisterGameAccountAsync(name);
            await logger.LogAsync($"level=INFO operation=account-register userId={result.User.Id} outcome=success");
            await codec.WriteApiResultAsync(context, new object?[] { result.LoginToken, 0 }, config.RegisterResponseMode);
        });

        MapAccountAuthenticationEndpoints(app, state);

        app.MapPost("/localap/api/Account/GetPushNotificationToken", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var pushToken = await state.UserDataService.GetCurrentUserPushTokenAsync(context);
            await logger.LogAsync($"get-push-notification-token payload={ValueFormatter.Format(body)} token={pushToken}");
            await codec.WriteApiResultAsync(context, new object?[] { pushToken }, "direct");
        });

        MapAccountRecoveryEndpoints(app,state);

        app.MapMethods("/localap/api/Account/GetCurrentUserData", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("account-get-current-user-data");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetCurrentUserResultAsync(context));
        });

        app.MapMethods("/localap/api/Account/UpdateBirthDate", new[] { "GET", "POST" }, async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var presentData = await state.UserDataService.UpdateBirthDateAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                presentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });

        app.MapMethods("/localap/api/Account/UpdateBirthDateAsync", new[] { "GET", "POST" }, async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var presentData = await state.UserDataService.UpdateBirthDateAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                presentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Login/Login", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"login payload={ValueFormatter.Format(body)}");
            var presentData = await state.UserDataService.GetLoginPresentDataAsync(context);
            await codec.WriteApiFramesAsync(context, GameResults.EmptyLogin(), presentData, Array.Empty<object?>(), Array.Empty<object?>(), "login-official-five-frame");
        });

        app.MapPost("/localap/api/Login", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"login-short payload={ValueFormatter.Format(body)}");
            var presentData = await state.UserDataService.GetLoginPresentDataAsync(context);
            await codec.WriteApiFramesAsync(context, GameResults.EmptyLogin(), presentData, Array.Empty<object?>(), Array.Empty<object?>(), "login-official-five-frame");
        });

        app.MapPost("/localap/api/Data/GetUserData", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"get-user-data payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetUserDataAsync(context), "lz4-five-frame");
        });

        app.MapMethods("/localap/api/data/user", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("data-user");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetUserDataAsync(context), "lz4-five-frame");
        });

        app.MapMethods("/localap/api/Profiles/Edit", new[] { "GET", "POST" }, async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"profile-edit payload={ValueFormatter.Format(body)}");
            var presentData = await state.UserDataService.EditProfileAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                presentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapMethods("/localap/api/Profile/Edit", new[] { "GET", "POST" }, async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"profile-edit-short payload={ValueFormatter.Format(body)}");
            var presentData = await state.UserDataService.EditProfileAsync(context, body);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { true },
                presentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapMethods("/localap/api/Profiles/GetCurrent", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("profiles-get-current");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetCurrentUserProfileDetailAsync(context));
        });

        app.MapPost("/localap/api/Data/GetMasterData", async context =>
        {
            var masterData = await state.MasterDataService.GetActiveAsync();
            await codec.WriteApiResultAsync(context, GameResults.MasterDataManifest(masterData), "lz4-five-frame");
        });

        app.MapMethods("/localap/api/data/master", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("data-master manifest");
            var masterData = await state.MasterDataService.GetActiveAsync();
            await codec.WriteApiResultAsync(context, GameResults.MasterDataManifest(masterData), "lz4-five-frame");
        });

        app.MapPost("/localap/api/Episodes/{episodeMasterId:long}/GetDetails", async (HttpContext context, long episodeMasterId) =>
        {
            await logger.LogAsync($"episodes-get-details episodeMasterId={episodeMasterId} query={context.Request.QueryString}");
            await codec.WriteApiResultAsync(context, TutorialEpisodeResults.Summary(episodeMasterId), "lz4-five-frame");
        });

        app.MapPost("/localap/api/Episodes/{episodeMasterId:long}/GetDetailsAsync", async (HttpContext context, long episodeMasterId) =>
        {
            await logger.LogAsync($"episodes-get-details-async episodeMasterId={episodeMasterId} query={context.Request.QueryString}");
            await codec.WriteApiResultAsync(context, TutorialEpisodeResults.Summary(episodeMasterId), "lz4-five-frame");
        });

        app.MapMethods("/localap/api/Episodes/{episodeMasterId:long}/DetailAssetSource", new[] { "GET", "POST" }, async (HttpContext context, long episodeMasterId) =>
        {
            codec.SetCommonHeaders(context);
            context.Response.ContentType = "application/vnd.msgpack";
            var result = TutorialEpisodeResults.Details(episodeMasterId);
            var payload = MsgPack.Encode(result);
            context.Response.Headers.ContentLength = payload.Length;
            await logger.LogAsync($"episodes-detail-asset-source episodeMasterId={episodeMasterId} bytes={payload.Length} result={ValueFormatter.Format(result)}");
            await context.Response.Body.WriteAsync(payload);
        });

        MapEpisodeReadEndpoints(app, state, codec, logger);

        MapHomePlayerEventEndpoints(app, state, codec, logger);

        app.MapPost("/localap/api/Events/DugongRun/{dugongRunCourseMasterId:long}/Clear/{clearResult}", async (HttpContext context, long dugongRunCourseMasterId, string clearResult) =>
        {
            var clearType = ReadIntQuery(context, "clearType")
                            ?? clearResult.ToLowerInvariant() switch
                            {
                                "nomisstake" => 2,
                                "clear" => 1,
                                _ => 0
                            };
            await logger.LogAsync($"events-dugong-run-clear dugongRunCourseMasterId={dugongRunCourseMasterId} clearResult={clearResult} clearType={clearType} query={context.Request.QueryString}");
            var result = await state.UserDataService.ClearDugongRunCourseAsync(context, dugongRunCourseMasterId, clearType);
            await codec.WriteApiFramesAsync(
                context,
                result.Rewards,
                result.PresentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "five-frame");
        });

        MapGameplayEndpoints(app, state, codec, logger);

        MapInboxFriendEndpoints(app, state, codec, logger);

        MapCompetitionEndpoints(app, state, codec, logger);

        MapCommerceMediaCircleEndpoints(app, state, codec, logger);
        MapCircleEndpoints(app, state, codec, logger);

    }

    static long ReadNotificationId(HttpContext context, object? body)
    {
        if (context.Request.RouteValues.TryGetValue("mNotificationId", out var routeValue)
            && long.TryParse(routeValue?.ToString(), out var routeId))
        {
            return routeId;
        }

        if (long.TryParse(context.Request.Query["mNotificationId"].FirstOrDefault(), out var queryId)
            || long.TryParse(context.Request.Query["id"].FirstOrDefault(), out queryId))
        {
            return queryId;
        }

        return ExtractFirstLong(body) ?? 0L;
    }

    static IEnumerable<long> ExtractLongs(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
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

    static long? ExtractFirstLong(object? value)
    {
        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            string text when long.TryParse(text, out var parsed) => parsed,
            object?[] array => array.Select(ExtractFirstLong).FirstOrDefault(v => v is not null),
            _ => null
        };
    }

    static long? ReadLongQuery(HttpContext context, string key)
    {
        return long.TryParse(context.Request.Query[key].FirstOrDefault(), out var value)
            ? value
            : null;
    }

    static int? ReadIntQuery(HttpContext context, string key)
    {
        return int.TryParse(context.Request.Query[key].FirstOrDefault(), out var value)
            ? value
            : null;
    }

    static object?[] BuildGameHintReadData(object? body)
    {
        var category = (int)(ExtractFirstLong(body) ?? 0L);
        return new object?[]
        {
            66,
            new object?[]
            {
                Random.Shared.Next(10000000, 99999999),
                category,
                true
            }
        };
    }

    static object?[] BuildShopLastViewedData(object? body)
    {
        var values = body as object?[];
        var shopCategory = (int)(ExtractFirstLong(values?.ElementAtOrDefault(0) ?? body) ?? 0L);
        var shopMasterId = ExtractFirstLong(values?.ElementAtOrDefault(1));
        return new object?[]
        {
            65,
            new object?[]
            {
                Random.Shared.Next(10000000, 99999999),
                shopMasterId,
                DateTime.UtcNow,
                shopCategory
            }
        };
    }

    static object?[] BuildAuthenticateResult(string apiToken, AuthenticatePayload payload, LocalConfig config)
    {
        return new object?[]
        {
            apiToken,
            ReadNumericGameVersion(payload.GameVersion),
            string.IsNullOrWhiteSpace(payload.ApkHash) ? null : payload.ApkHash,
            string.IsNullOrWhiteSpace(payload.ApkApplicationSignature) ? null : payload.ApkApplicationSignature,
            string.IsNullOrWhiteSpace(payload.ApplicationVersion) ? config.ApplicationVersion : payload.ApplicationVersion
        };
    }

    static object ReadNumericGameVersion(string value)
    {
        return int.TryParse(value, out var number) ? number : value;
    }
}


