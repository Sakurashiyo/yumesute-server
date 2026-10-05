static partial class ApiEndpointMappings
{
    static void MapAccountAuthenticationEndpoints(WebApplication app, LocalServerState state)
    {
        // 玩家接口共享认证失败边界，避免无效会话被当作服务器内部错误。
        app.Use(async (context, next) =>
        {
            try { await next(); }
            catch (BadHttpRequestException error) when (!context.Response.HasStarted
                && error.StatusCode is 400 or 401 or 429
                && error.Message is AccountErrors.InvalidLoginToken or AccountErrors.Unauthorized
                    or AccountErrors.InvalidConfirmationCode or AccountErrors.InvalidTakeOverPassword
                    or AccountErrors.InvalidTakeOverCredentials or AccountErrors.InvalidRecoveryRequest or AccountErrors.RecoveryRateLimited
                    or NotificationErrors.InvalidReadRequest)
            {
                await state.Logger.LogAsync($"level=WARN operation=account-authentication path={context.Request.Path} requestId={context.TraceIdentifier} errorCode={error.Message} outcome=failed");
                await state.Codec.WriteApiErrorAsync(context, error.Message, error.StatusCode);
            }
        });

        foreach (var path in new[] { "/localap/api/Account/Authenticate", "/localap/api/Account/AuthenticateAndSetApiToken" })
        {
            app.MapPost(path, async context =>
            {
                var bytes = await state.Codec.ReadRequestBytesAsync(context);
                var payload = ReadAccountAuthenticationPayload(bytes, state.Config.ApplicationVersion);
                var result = await state.AccountService.AuthenticateClientAsync(payload);
                await state.Logger.LogAsync($"level=INFO operation=account-authentication userId={result.User.Id} requestId={context.TraceIdentifier} outcome=success");
                await state.Codec.WriteApiResultAsync(context, new object?[] { result.ApiToken, 0, null }, "lz4-five-frame");
            });
        }
    }

    static AuthenticatePayload ReadAccountAuthenticationPayload(byte[] bytes, string applicationVersion)
    {
        try { return PayloadReaders.ReadAuthenticatePayloadFromRaw(bytes, applicationVersion); }
        catch (Exception error) when (error is InvalidDataException or FormatException
            or IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
        {
            // 只转换解码边界的非法外部输入，不捕获数据库或业务处理失败。
            throw new BadHttpRequestException(AccountErrors.InvalidLoginToken, StatusCodes.Status401Unauthorized);
        }
    }
}
