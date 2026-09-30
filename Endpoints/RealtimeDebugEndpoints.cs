using SiriusLocalServer.Realtime;

static class RealtimeDebugEndpoints
{
    public static void MapRealtimeDebugEndpoints(this WebApplication app, LocalServerState state)
    {
        app.MapGet("/realtime/debug/multi-live/rooms", () =>
        {
            return Results.Json(state.MultiLiveRealtime.SnapshotRooms());
        });

        app.MapPost("/realtime/debug/multi-live/join-public", async (HttpContext context) =>
        {
            var hashUserId = ReadString(context, "hashUserId", "local-user");
            var userName = ReadString(context, "userName", "LocalPlayer");
            var liveSettingMasterId = ReadLong(context, "liveSettingMasterId", 7501);
            var hallType = (MultiLiveHallType)ReadInt(context, "hallType", (int)MultiLiveHallType.Sirius);
            var result = await state.MultiLiveRealtime.JoinPublicHallAsync(
                hashUserId,
                hallType,
                liveSettingMasterId,
                null,
                userName,
                180001,
                MultiLiveCharacter.Default(110040));
            return Results.Json(result);
        });

        app.MapPost("/realtime/debug/multi-live/create-private", async (HttpContext context) =>
        {
            var hashUserId = ReadString(context, "hashUserId", "local-user");
            var userName = ReadString(context, "userName", "LocalPlayer");
            var liveSettingMasterId = ReadLong(context, "liveSettingMasterId", 7501);
            var hallType = (MultiLiveHallType)ReadInt(context, "hallType", (int)MultiLiveHallType.Sirius);
            var result = await state.MultiLiveRealtime.CreatePrivateHallAsync(
                hashUserId,
                hallType,
                liveSettingMasterId,
                null,
                userName,
                180001,
                MultiLiveCharacter.Default(110040));
            return Results.Json(result);
        });

        app.MapGet("/realtime/debug/multi-live/fetch-users", async (HttpContext context) =>
        {
            var hashUserId = ReadString(context, "hashUserId", "local-user");
            return Results.Json(await state.MultiLiveRealtime.FetchUsersAsync(hashUserId));
        });

        app.MapPost("/realtime/debug/multi-live/select-music", async (HttpContext context) =>
        {
            var hashUserId = ReadString(context, "hashUserId", "local-user");
            var musicId = ReadLong(context, "musicId", 75);
            var isRandom = ReadBool(context, "isRandom", false);
            var isAfk = ReadBool(context, "isAFK", false);
            await state.MultiLiveRealtime.SelectMusicAsync(hashUserId, musicId, isRandom, isAfk);
            return Results.Json(await state.MultiLiveRealtime.FetchUsersAsync(hashUserId));
        });

        app.MapPost("/realtime/debug/multi-live/select-difficulty", async (HttpContext context) =>
        {
            var hashUserId = ReadString(context, "hashUserId", "local-user");
            var difficulty = (MusicDifficulties)ReadInt(context, "difficulty", (int)MusicDifficulties.Extra);
            var isAfk = ReadBool(context, "isAFK", false);
            await state.MultiLiveRealtime.SelectDifficultyAsync(hashUserId, difficulty, isAfk);
            return Results.Json(await state.MultiLiveRealtime.FetchUsersAsync(hashUserId));
        });

        app.MapPost("/realtime/debug/multi-live/ready-game", async (HttpContext context) =>
        {
            var hashUserId = ReadString(context, "hashUserId", "local-user");
            await state.MultiLiveRealtime.ReadyGameAsync(hashUserId);
            return Results.Json(await state.MultiLiveRealtime.FetchUsersAsync(hashUserId));
        });

        app.MapPost("/realtime/debug/multi-live/start-game", async (HttpContext context) =>
        {
            var hashUserId = ReadString(context, "hashUserId", "local-user");
            await state.MultiLiveRealtime.BeforeGameCalculateAsync(hashUserId);
            await state.MultiLiveRealtime.StartGameAsync(hashUserId);
            return Results.Json(await state.MultiLiveRealtime.FetchUsersAsync(hashUserId));
        });

        app.MapPost("/realtime/debug/multi-live/sync", async (HttpContext context) =>
        {
            var hashUserId = ReadString(context, "hashUserId", "local-user");
            var combo = ReadInt(context, "combo", 100);
            var comboType = (ComboTypes)ReadInt(context, "comboType", (int)ComboTypes.None);
            var life = ReadInt(context, "life", 1000);
            await state.MultiLiveRealtime.SyncInGameStatusAsync(hashUserId, combo, comboType, life);
            return Results.Json(await state.MultiLiveRealtime.FetchUsersAsync(hashUserId));
        });

        app.MapPost("/realtime/debug/multi-live/exit-game", async (HttpContext context) =>
        {
            var hashUserId = ReadString(context, "hashUserId", "local-user");
            var score = ReadLong(context, "score", 1000000);
            var clearLamp = (ClearLamps)ReadInt(context, "clearLamp", (int)ClearLamps.Clear);
            var maxCombo = ReadInt(context, "maxCombo", 300);
            await state.MultiLiveRealtime.ExitGameAsync(hashUserId, score, clearLamp, null, maxCombo);
            return Results.Json(await state.MultiLiveRealtime.FetchUsersAsync(hashUserId));
        });

        app.MapGet("/realtime/debug/multi-live/information/{multiLiveId:long}", (long multiLiveId) =>
        {
            return Results.Json(state.MultiLiveRealtime.GetMultiLiveInformation(multiLiveId));
        });
    }

    static string ReadString(HttpContext context, string name, string defaultValue)
    {
        return context.Request.Query.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : defaultValue;
    }

    static int ReadInt(HttpContext context, string name, int defaultValue)
    {
        return context.Request.Query.TryGetValue(name, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;
    }

    static long ReadLong(HttpContext context, string name, long defaultValue)
    {
        return context.Request.Query.TryGetValue(name, out var value) && long.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;
    }

    static bool ReadBool(HttpContext context, string name, bool defaultValue)
    {
        return context.Request.Query.TryGetValue(name, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;
    }
}
