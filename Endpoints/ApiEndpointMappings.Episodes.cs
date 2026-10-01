static partial class ApiEndpointMappings
{
    static void MapEpisodeReadEndpoints(WebApplication app, LocalServerState state, ApiCodec codec, LocalRequestLogger logger)
    {
        foreach (var route in new[] { "Read", "ReadAll", "ReadAsync", "ReadAllAsync" })
        {
            var hasReadAll = route is "ReadAll" or "ReadAllAsync";
            app.MapPost($"/localap/api/Episodes/{{episodeMasterId:long}}/{route}", async (HttpContext context, long episodeMasterId) =>
            {
                try
                {
                    var result = await state.UserDataService.ReadEpisodeAsync(context, episodeMasterId, hasReadAll);
                    await codec.WriteApiFramesAsync(context, result.Rewards, result.PresentData,
                        Array.Empty<object?>(), result.Notifications, "present-lz4-when-not-empty-five-frame");
                }
                catch (BadHttpRequestException error)
                {
                    await logger.LogAsync($"level=WARN operation=episode-read episodeId={episodeMasterId} errorCode={error.Message} outcome=failed");
                    context.Response.StatusCode = error.StatusCode;
                    await codec.WriteApiFramesAsync(context, Array.Empty<object?>(), Array.Empty<object?>(),
                        Array.Empty<object?>(), Array.Empty<object?>(), "five-frame");
                }
            });
        }
    }
}
