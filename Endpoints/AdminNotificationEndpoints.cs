static class AdminNotificationEndpoints
{
    public static void MapAdminNotificationEndpoints(this WebApplication app, LocalServerState state)
    {
        app.MapGet("/admin/notifications", () => Results.Redirect("/admin/operations/#notices"));
    }
}
