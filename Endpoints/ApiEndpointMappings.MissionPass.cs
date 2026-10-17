static partial class ApiEndpointMappings
{
    static void MapMissionPassEndpoints(WebApplication app, LocalServerState state)
    {
        var group = app.MapGroup("/localap/api/Missions");
        group.AddEndpointFilter(async (invocation, next) =>
        {
            try { return await next(invocation); }
            catch (BadHttpRequestException error) when (error.Message is
                MissionPassErrors.Invalid or MissionPassErrors.Unavailable or MissionPassErrors.Insufficient or MissionPassErrors.PointLimit)
            {
                var context = invocation.HttpContext;
                await state.Logger.LogAsync(
                    $"level=WARN operation=mission-pass path={context.Request.Path} requestId={context.TraceIdentifier} errorCode={error.Message} outcome=failed");
                await state.Codec.WriteApiErrorAsync(context, error.Message, error.StatusCode);
                return Results.Empty;
            }
        });
        group.MapPost("/MissionPassReceiveRewards/{missionPassId:long}", async (HttpContext context, long missionPassId) =>
        {
            var claim = await state.UserDataService.ReceiveMissionPassAsync(context, missionPassId);
            await state.Codec.WriteApiFramesAsync(context, claim.Result, claim.Present,
                Array.Empty<object?>(), Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });
        group.MapPost("/ExchangeMissionPoint", async (HttpContext context) =>
        {
            if (!int.TryParse(context.Request.Query["unit"], out var unit))
                throw new BadHttpRequestException(MissionPassErrors.Invalid, 400);
            var exchange = await state.UserDataService.ExchangeMissionPointAsync(context, unit);
            await state.Codec.WriteApiFramesAsync(context, exchange.Result, exchange.Present,
                Array.Empty<object?>(), Array.Empty<object?>(), "present-lz4-when-not-empty-five-frame");
        });
    }
}
