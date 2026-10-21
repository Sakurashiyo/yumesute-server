static partial class ApiEndpointMappings
{
    static void MapAuditionEndpoints(WebApplication app, LocalServerState state, ApiCodec codec, LocalRequestLogger logger)
    {
        app.Use(async (context, next) =>
        {
            try { await next(); }
            catch (BadHttpRequestException error) when (!context.Response.HasStarted
                && error.Message is AuditionErrors.NotFound or AuditionErrors.InvalidStart
                    or AuditionErrors.PartyMissing or AuditionErrors.PartyUnavailable)
            {
                await logger.LogAsync($"level=WARN operation=audition path={context.Request.Path} requestId={context.TraceIdentifier} errorCode={error.Message} outcome=failed");
                await codec.WriteApiErrorAsync(context, error.Message, error.StatusCode);
            }
        });
        app.MapGet("/localap/api/Auditions/{auditionId:long}", async (HttpContext context, long auditionId) =>
        {
            var result = await state.UserDataService.GetAuditionInformationAsync(context, auditionId);
            await logger.LogAsync($"level=INFO operation=audition-information auditionId={auditionId} outcome=success");
            await codec.WriteApiResultAsync(context, result, "five-frame");
        });
        app.MapGet("/localap/api/Auditions/{auditionId:long}/HighScoreParty", async (HttpContext context, long auditionId) =>
        {
            var result = await state.UserDataService.GetAuditionHighScorePartyAsync(context, auditionId);
            await codec.WriteApiResultAsync(context, result, "five-frame");
        });
        app.MapPost("/localap/api/Auditions/{auditionId:long}/CopyTo/{uPartyId:long}",
            async (HttpContext context, long auditionId, long uPartyId) =>
        {
            var result = await state.UserDataService.CopyAuditionClearPartyAsync(context, auditionId, uPartyId);
            await codec.WriteApiFramesAsync(context, result.Result, result.Present,
                Array.Empty<object?>(), Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });
    }
}
