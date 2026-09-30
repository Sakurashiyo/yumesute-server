static partial class ApiEndpointMappings
{
    static void MapLessonEndpoints(WebApplication app, LocalServerState state, ApiCodec codec, LocalRequestLogger logger)
    {
        async Task WriteLessonResult(HttpContext context, string operation, Func<Task<object?[]>> action)
        {
            object?[] present;
            try { present = await action(); }
            catch (BadHttpRequestException error)
            {
                await logger.LogAsync($"level=WARN operation={operation} outcome=failed errorCode={error.Message}");
                context.Response.StatusCode = error.StatusCode;
                await codec.WriteApiFramesAsync(context, new object?[] { false }, Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>());
                return;
            }
            await logger.LogAsync($"operation={operation} outcome=success");
            await codec.WriteApiFramesAsync(context, new object?[] { true }, present, Array.Empty<object?>(), Array.Empty<object?>());
        }

        app.MapPost("/localap/api/Lessons/{characterBaseMasterId:long}/CreateParty", (HttpContext context, long characterBaseMasterId) =>
            WriteLessonResult(context, "lessons-create-party", () => state.UserDataService.CreateLessonPartyAsync(context, characterBaseMasterId)));

        app.MapPost("/localap/api/Lessons/{characterBaseMasterId:long}/SetParty", async (HttpContext context, long characterBaseMasterId) =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await WriteLessonResult(context, "lessons-set-party", () => state.UserDataService.SetLessonPartyAsync(context, characterBaseMasterId, body));
        });

        app.MapPost("/localap/api/Lessons/{characterBaseMasterId:long}/SetPartyLeader/{leaderPosition:int}", (HttpContext context, long characterBaseMasterId, int leaderPosition) =>
            WriteLessonResult(context, "lessons-set-party-leader", () => state.UserDataService.SetLessonPartyLeaderAsync(context, characterBaseMasterId, leaderPosition)));

        app.MapPost("/localap/api/Lessons/{characterBaseMasterId:long}/Start/{liveMasterId:long}", async (HttpContext context, long characterBaseMasterId, long liveMasterId) =>
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await WriteLessonResult(context, "lessons-start", () => state.UserDataService.StartLessonAsync(context, characterBaseMasterId, liveMasterId, body));
        });

        async Task FinishLesson(HttpContext context)
        {
            var body = await codec.ReadRequestBodyAsync(context);
            await WriteLessonResult(context, "lessons-finish", () => state.UserDataService.FinishLessonAsync(context, body));
        }
        app.MapPost("/localap/api/Lessons/Finish", FinishLesson);
    }
}
