static partial class ApiEndpointMappings
{
    static void MapCircleEndpoints(WebApplication app, LocalServerState state, ApiCodec codec, LocalRequestLogger logger)
    {
        app.MapPost("/localap/api/Circles/JoinPreRequest", async context =>
            await codec.WriteApiResultAsync(context,
                await state.UserDataService.CheckCircleJoinAsync(context, context.Request.Query["circleId"].ToString()), "five-frame"));
        app.MapPost("/localap/api/Circles/Join", async context =>
        {
            var joined = await state.UserDataService.JoinCircleAsync(context, context.Request.Query["circleId"].ToString());
            await codec.WriteApiFramesAsync(context, joined.Result, joined.Present, Array.Empty<object?>(),
                Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });
        app.MapPost("/localap/api/Circles/Create", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var created = await state.UserDataService.CreateCircleAsync(context, body);
            await codec.WriteApiFramesAsync(context, created.Result, created.Present, Array.Empty<object?>(),
                Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });
        app.MapPost("/localap/api/Circles/EditCircleBanner", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await codec.WriteApiResultAsync(context, await state.UserDataService.EditCircleBannerAsync(context, body), "five-frame");
        });
        app.MapPost("/localap/api/Circles/SetIsPublishRanking/{isPublishRanking}", async context =>
            await codec.WriteApiResultAsync(context,
                await state.UserDataService.SetCircleRankingPublishedAsync(context,
                    context.Request.RouteValues["isPublishRanking"]?.ToString()), "five-frame"));
        app.MapMethods("/localap/api/Circles", new[] { "GET", "POST" }, async context =>
        {
            // 客户端用同一路由的 circleId 查询参数读取单个社团，而不是 /{id}。
            var circleId = context.Request.Query["circleId"].FirstOrDefault();
            if (!string.IsNullOrEmpty(circleId))
            {
                await codec.WriteApiResultAsync(context, await state.UserDataService.GetCircleAsync(circleId), "five-frame");
                return;
            }
            var circles = await state.UserDataService.GetCirclesAsync();
            await logger.LogAsync($"circles-list count={circles.Length}");
            await codec.WriteApiResultAsync(context, circles, "five-frame");
        });
        // 社团读取也有 POST 调用，GET/POST 共用处理器，避免客户端切换页面时落入 404。
        app.MapMethods("/localap/api/Circles/MyCircleInfo", new[] { "GET", "POST" }, async context =>
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetMyCircleAsync(context), "five-frame"));
        app.MapMethods("/localap/api/Circles/MemberInfo", new[] { "GET", "POST" }, async context =>
            await codec.WriteApiResultAsync(context,
                await state.UserDataService.GetCircleMembersAsync(context.Request.Query["circleId"].ToString()), "five-frame"));
        app.MapMethods("/localap/api/Circles/Search/Request", new[] { "GET", "POST" }, async context =>
            await codec.WriteApiResultAsync(context,
                await state.UserDataService.GetCircleJoinRequestsAsync(context, context.Request.Query["circleId"].ToString()), "five-frame"));
        app.MapMethods("/localap/api/Circles/Search/Friend", new[] { "GET", "POST" }, async context =>
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetCircleFriendsAsync(context), "five-frame"));
        app.MapMethods("/localap/api/Circles/Search/Inviting", new[] { "GET", "POST" }, async context =>
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetCircleInvitingAsync(context), "five-frame"));
        app.MapMethods("/localap/api/Circles/SearchUser", new[] { "GET", "POST" }, async context =>
            await codec.WriteApiResultAsync(context,
                await state.UserDataService.SearchCircleUserAsync(context, context.Request.Query["targetUserId"].ToString()), "five-frame"));
        app.MapPost("/localap/api/Circles/ReceiveTheaterStamina", async context =>
        {
            var received = await state.UserDataService.ReceiveCircleTheaterStaminaAsync(context);
            await codec.WriteApiFramesAsync(context, received.Result, received.Present,
                Array.Empty<object?>(), Array.Empty<object?>(), "five-frame");
        });
        app.MapMethods("/localap/api/Circles/Invited", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("circles-invited");
            await codec.WriteApiResultAsync(context, Array.Empty<object?>(), "five-frame");
        });
        app.MapMethods("/localap/api/Circles/RecommendUsers", new[] { "GET", "POST" }, async context =>
            await codec.WriteApiResultAsync(context,
                await state.UserDataService.GetCircleRecommendedUsersAsync(context,context.Request.Query["circleId"].ToString()),"five-frame"));
        app.MapMethods("/localap/api/Circles/GetSupportAndTheaterLevelInformation", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("circles-get-support-and-theater-level-information");
            await codec.WriteApiResultAsync(context, GameResults.CircleSupportAndTheaterLevelInformation(), "lz4-five-frame");
        });
    }
}
