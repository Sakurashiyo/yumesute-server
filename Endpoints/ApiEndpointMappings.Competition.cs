static partial class ApiEndpointMappings
{
    static void MapCompetitionEndpoints(WebApplication app, LocalServerState state, ApiCodec codec, LocalRequestLogger logger)
    {
        app.MapGet("/localap/api/Leagues", async context =>
        {
            await logger.LogAsync("leagues-list");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.LeagueListResult(),
                GameResults.LeagueListPresentData(),
                Array.Empty<object?>(),
                Array.Empty<object?>());
        });

        app.MapPost("/localap/api/Leagues/TopMenuInformation", async context =>
        {
            await logger.LogAsync("leagues-top-menu-information");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.LeagueTopMenuInformation(),
                await state.UserDataService.BuildLeagueTopMenuPresentDataAsync(context),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });

        app.MapPost("/localap/api/Leagues/GroupRanking/{leagueMasterId:long}/{leagueGroupId:long}", async (HttpContext context, long leagueMasterId, long leagueGroupId) =>
        {
            await logger.LogAsync($"leagues-group-ranking leagueMasterId={leagueMasterId} leagueGroupId={leagueGroupId} query={context.Request.QueryString}");
            await codec.WriteApiFramesAsync(
                context,
                await state.UserDataService.BuildLeagueGroupRankingResultAsync(context, leagueMasterId),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "result-lz4-five-frame");
        });

        app.MapGet("/localap/api/TripleCast", async context =>
        {
            await logger.LogAsync("triple-cast-list");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.TripleCastListResult(),
                GameResults.TripleCastListPresentData(),
                Array.Empty<object?>(),
                Array.Empty<object?>());
        });

        app.MapPost("/localap/api/TripleCast/TopMenuInformation", async context =>
        {
            await logger.LogAsync("triple-cast-top-menu-information");
            await codec.WriteApiFramesAsync(
                context,
                GameResults.TripleCastTopMenuInformation(),
                await state.UserDataService.BuildTripleCastTopMenuPresentDataAsync(context),
                Array.Empty<object?>(),
                Array.Empty<object?>(),
                "present-lz4-when-not-empty-five-frame");
        });
    }
}
