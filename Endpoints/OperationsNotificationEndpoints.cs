static class OperationsNotificationEndpoints
{
    public static void MapOperationsNotifications(this RouteGroupBuilder group, LocalServerState state, ILogger logger)
    {
        var banners = new NotificationBannerCatalog(state.Config);
        var service = new NotificationAdminService(state.Database, banners);
        group.MapGet("/notifications.js", () => Results.Stream(
            typeof(OperationsNotificationEndpoints).Assembly.GetManifestResourceStream("AdminWeb.notifications.js")!, "text/javascript"));
        group.MapGet("/api/notifications", async () => Results.Json(await service.ListAsync(), GameOperationsJson.Options));
        group.MapGet("/api/notification-banners", () => banners.Available());
        group.MapGet("/api/notification-banners/{key}", (string key) =>
        {
            var banner = banners.Available().SingleOrDefault(banner => banner.Key == key);
            return banner is null ? Results.NotFound() : Results.File(NotificationBannerCatalog.PreviewFile(banner), "image/png");
        });
        group.MapPost("/api/notifications", async (HttpContext context) =>
        {
            var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = 98304;
            var row = await context.Request.ReadFromJsonAsync<NotificationAdminRow>(GameOperationsJson.Options)
                ?? throw new GameOperationsException(NotificationAdminErrors.Invalid, "缺少公告");
            var id = await service.SaveAsync(row);
            logger.LogInformation("公告保存完成 operation={Operation} entityId={EntityId} requestId={RequestId}",
                "notification-save", id, context.TraceIdentifier);
            return Results.Json(new { id });
        });
    }
}
