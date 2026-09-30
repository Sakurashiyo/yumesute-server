sealed partial class UserDataService
{
    public async Task<object?[]> BuildLeagueTopMenuPresentDataAsync(HttpContext context)
    {
        var player = await ReadCompetitionPlayerAsync(context);
        return GameResults.LeagueTopMenuPresentData(player.User, player.Profile);
    }

    public async Task<object?[]> BuildTripleCastTopMenuPresentDataAsync(HttpContext context)
    {
        var player = await ReadCompetitionPlayerAsync(context);
        return GameResults.TripleCastTopMenuPresentData(player.User, player.Profile);
    }

    public async Task<object?[]> BuildLeagueGroupRankingResultAsync(HttpContext context, long leagueMasterId)
    {
        var player = await ReadCompetitionPlayerAsync(context);
        return GameResults.LeagueGroupRankingResult(leagueMasterId, player.PublicId, player.DisplayName);
    }

    async Task<CompetitionPlayerSnapshot> ReadCompetitionPlayerAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null)
        {
            var fallbackData = GameResults.UserData();
            var fallbackUser = (fallbackData.ElementAtOrDefault(0) as object?[])?.ElementAtOrDefault(1) as object?[] ?? Array.Empty<object?>();
            var fallbackProfile = (fallbackData.ElementAtOrDefault(1) as object?[])?.ElementAtOrDefault(1) as object?[] ?? Array.Empty<object?>();
            return new CompetitionPlayerSnapshot("0000000000", "LocalPlayer", fallbackUser, fallbackProfile);
        }

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        var user = await ReadUserAsync(connection, userId.Value);
        var profile = await ReadProfileAsync(connection, userId.Value);
        var publicId = user.ElementAtOrDefault(13) as string ?? FormatNumericId(userId.Value);
        var displayName = profile.ElementAtOrDefault(1) as string ?? "LocalPlayer";
        return new CompetitionPlayerSnapshot(publicId, displayName, user, profile);
    }

    sealed record CompetitionPlayerSnapshot(string PublicId, string DisplayName, object?[] User, object?[] Profile);
}
