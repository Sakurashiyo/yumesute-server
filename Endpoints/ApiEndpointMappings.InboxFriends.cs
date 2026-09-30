static partial class ApiEndpointMappings
{
    static void MapInboxFriendEndpoints(WebApplication app, LocalServerState state, ApiCodec codec, LocalRequestLogger logger)
    {
        app.MapPost("/localap/api/Inboxes/CheckPackagesAsync", async context =>
        {
            var result = await state.UserDataService.CheckInboxPackagesAsync(context);
            await logger.LogAsync($"inboxes-check-packages-async isSuccess={result.IsSuccess} presentCount={result.PresentData.Length}");
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { result.IsSuccess },
                result.PresentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Inboxes/CheckPackages", async context =>
        {
            var hasPackages = await state.UserDataService.HasUnreadInboxPackagesAsync(context);
            await logger.LogAsync($"inboxes-check-packages-sync hasPackages={hasPackages}");
            await codec.WriteApiResultAsync(context, new object?[] { hasPackages });
        });

        app.MapPost("/localap/api/Inboxes/CheckPackagesWithRetry", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var hasPackages = await state.UserDataService.HasUnreadInboxPackagesAsync(context);
            await logger.LogAsync($"inboxes-check-packages-with-retry hasPackages={hasPackages} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, new object?[] { hasPackages });
        });

        app.MapPost("/localap/api/Inboxes/CheckPackagesWithRetryAsync", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var hasPackages = await state.UserDataService.HasUnreadInboxPackagesAsync(context);
            await logger.LogAsync($"inboxes-check-packages-with-retry-async hasPackages={hasPackages} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, new object?[] { hasPackages });
        });

        app.MapPost("/localap/api/Inbox/CheckPackagesAsync", async context =>
        {
            var hasPackages = await state.UserDataService.HasUnreadInboxPackagesAsync(context);
            await logger.LogAsync($"inbox-check-packages hasPackages={hasPackages}");
            await codec.WriteApiResultAsync(context, new object?[] { hasPackages });
        });

        app.MapPost("/localap/api/Inbox/CheckPackages", async context =>
        {
            var hasPackages = await state.UserDataService.HasUnreadInboxPackagesAsync(context);
            await logger.LogAsync($"inbox-check-packages-sync hasPackages={hasPackages}");
            await codec.WriteApiResultAsync(context, new object?[] { hasPackages });
        });

        app.MapPost("/localap/api/Inbox/CheckPackagesWithRetry", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var hasPackages = await state.UserDataService.HasUnreadInboxPackagesAsync(context);
            await logger.LogAsync($"inbox-check-packages-with-retry hasPackages={hasPackages} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, new object?[] { hasPackages });
        });

        app.MapPost("/localap/api/Inbox/CheckPackagesWithRetryAsync", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var hasPackages = await state.UserDataService.HasUnreadInboxPackagesAsync(context);
            await logger.LogAsync($"inbox-check-packages-with-retry-async hasPackages={hasPackages} payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, new object?[] { hasPackages });
        });

        app.MapPost("/localap/api/Inboxes/BulkReceive", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var inboxIds = ExtractLongs(body).ToArray();
            await logger.LogAsync($"inboxes-bulk-receive ids=[{string.Join(",", inboxIds)}] payload={ValueFormatter.Format(body)}");
            var result = await state.UserDataService.ReceiveInboxPackagesAsync(context, inboxIds);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { result.Rewards, result.HasUnread },
                result.PresentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Inbox/BulkReceive", async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            var inboxIds = ExtractLongs(body).ToArray();
            await logger.LogAsync($"inbox-bulk-receive ids=[{string.Join(",", inboxIds)}] payload={ValueFormatter.Format(body)}");
            var result = await state.UserDataService.ReceiveInboxPackagesAsync(context, inboxIds);
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { result.Rewards, result.HasUnread },
                result.PresentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapMethods("/localap/api/Inboxes/{inboxId:long}/Receive", new[] { "GET", "POST" }, async (HttpContext context, long inboxId) =>
        {
            await logger.LogAsync($"inboxes-receive inboxId={inboxId} query={context.Request.QueryString}");
            var result = await state.UserDataService.ReceiveInboxPackagesAsync(context, new[] { inboxId });
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { result.Rewards, result.HasUnread },
                result.PresentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapMethods("/localap/api/Inbox/{inboxId:long}/Receive", new[] { "GET", "POST" }, async (HttpContext context, long inboxId) =>
        {
            await logger.LogAsync($"inbox-receive inboxId={inboxId} query={context.Request.QueryString}");
            var result = await state.UserDataService.ReceiveInboxPackagesAsync(context, new[] { inboxId });
            await codec.WriteApiFramesAsync(
                context,
                new object?[] { result.Rewards, result.HasUnread },
                result.PresentData,
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapMethods("/localap/api/ReceivedRequest", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("friends-received-request-short");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetIncomingFriendRequestsAsync(context), "five-frame");
        });

        app.MapMethods("/localap/api/SendingRequest", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("friends-sending-request-short");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetOutgoingFriendRequestsAsync(context), "five-frame");
        });

        app.MapMethods("/localap/api/FriendInvitation/Update", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("friend-invitation-update");
            await codec.WriteApiResultAsync(context, new object?[] { true }, "five-frame");
        });

        app.MapMethods("/localap/api/Friends/ReceivedRequest", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("friends-received-request");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetIncomingFriendRequestsAsync(context));
        });

        app.MapMethods("/localap/api/Friends", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("friends-list");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetFriendListResultAsync(context));
        });

        app.MapMethods("/localap/api/Friends/BlockUsers", new[] { "GET", "POST" }, async context =>
        {
            await logger.LogAsync("friends-block-users");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetBlockListResultAsync(context));
        });

        app.MapMethods("/localap/api/Friends/Search", new[] { "GET", "POST" }, async context =>
        {
            var targetUserId = context.Request.Query["targetUserId"].FirstOrDefault();
            await logger.LogAsync($"friends-search targetUserId={targetUserId}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.GetFriendSearchResultAsync(context, targetUserId));
        });

        app.MapMethods("/localap/api/Friends/SendRequest", new[] { "GET", "POST" }, async context =>
        {
            var targetUserId = context.Request.Query["targetUserId"].FirstOrDefault();
            await logger.LogAsync($"friends-send-request targetUserId={targetUserId}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.SendFriendRequestAsync(context, targetUserId));
        });

        app.MapMethods("/localap/api/Friends/AcceptRequest", new[] { "GET", "POST" }, async context =>
        {
            var fromUserId = context.Request.Query["fromUserId"].FirstOrDefault();
            await logger.LogAsync($"friends-accept-request fromUserId={fromUserId}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.AcceptFriendRequestAsync(context, fromUserId));
        });

        app.MapMethods("/localap/api/Friends/BlockUser", new[] { "GET", "POST" }, async context =>
        {
            var targetUserId = context.Request.Query["targetUserId"].FirstOrDefault();
            await logger.LogAsync($"friends-block-user targetUserId={targetUserId}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.BlockFriendUserAsync(context, targetUserId));
        });

        app.MapMethods("/localap/api/Friends/RemoveBlockUser", new[] { "GET", "POST" }, async context =>
        {
            var targetUserId = context.Request.Query["targetUserId"].FirstOrDefault();
            await logger.LogAsync($"friends-remove-block-user targetUserId={targetUserId}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.RemoveBlockFriendUserAsync(context, targetUserId));
        });

        app.MapMethods("/localap/api/Friends/CancelRequest", new[] { "GET", "POST" }, async context =>
        {
            var targetUserId = context.Request.Query["targetUserId"].FirstOrDefault();
            await logger.LogAsync($"friends-cancel-request targetUserId={targetUserId}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.CancelFriendRequestAsync(context, targetUserId));
        });

        app.MapMethods("/localap/api/Friends/DenyRequest", new[] { "GET", "POST" }, async context =>
        {
            var fromUserId = context.Request.Query["fromUserId"].FirstOrDefault();
            await logger.LogAsync($"friends-deny-request fromUserId={fromUserId}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.DenyFriendRequestAsync(context, fromUserId));
        });

        app.MapMethods("/localap/api/Friends/RemoveFriend", new[] { "GET", "POST" }, async context =>
        {
            var targetUserId = context.Request.Query["targetUserId"].FirstOrDefault();
            await logger.LogAsync($"friends-remove-friend targetUserId={targetUserId}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.RemoveFriendAsync(context, targetUserId));
        });

        app.MapMethods("/localap/api/Friends/SetFavorite", new[] { "GET", "POST" }, async context =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await logger.LogAsync($"friends-set-favorite payload={ValueFormatter.Format(body)}");
            await codec.WriteApiResultAsync(context, await state.UserDataService.SetFriendFavoriteAsync(context, body));
        });
    }
}
