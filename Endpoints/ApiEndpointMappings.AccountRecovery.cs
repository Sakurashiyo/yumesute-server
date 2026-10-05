static partial class ApiEndpointMappings
{
    static void MapAccountRecoveryEndpoints(WebApplication app, LocalServerState state)
    {
        var codec = state.Codec;
        app.MapMethods("/localap/api/Account/GetConfirmationCode", new[] { "GET", "POST" }, async context =>
        {
            var result = await state.AccountService.GetConfirmationCodeAsync(PayloadReaders.ReadApiToken(context));
            await state.Logger.LogAsync($"level=INFO operation=account-confirmation-issue requestId={context.TraceIdentifier} outcome=success");
            await codec.WriteApiResultAsync(context, result, "five-frame");
        });
        // 本地验证入口
        app.MapPost("/localap/api/Account/VerifyConfirmationCode", async context =>
        {
            var payload = await AccountRecoveryPayloads.ReadAsync(context, codec, "ConfirmationCode");
            await state.AccountService.VerifyConfirmationCodeAsync(PayloadReaders.ReadApiToken(context), payload[0]);
            await state.Logger.LogAsync($"level=INFO operation=account-confirmation-verify requestId={context.TraceIdentifier} outcome=success");
            await codec.WriteApiResultAsync(context, new object?[] { true }, "five-frame");
        });
        app.MapMethods("/localap/api/Account/RegisterTakeOverPassword", new[] { "GET", "POST" }, async context =>
        {
            var payload = await AccountRecoveryPayloads.ReadAsync(context, codec, "Password");
            var registered = await state.AccountService.RegisterTakeOverPasswordAsync(PayloadReaders.ReadApiToken(context), payload[0]);
            await state.Logger.LogAsync($"level=INFO operation=account-takeover-register userId={registered.UserId} requestId={context.TraceIdentifier} outcome=success");
            await codec.WriteApiFramesAsync(context, new object?[] { true, registered.LinkageCode },
                new object?[] { new object?[] { 120, new object?[] { registered.UserId } } }, Array.Empty<object?>(), Array.Empty<object?>());
        });
        app.MapPost("/localap/api/Account/GetTakeOverAccount", async context =>
        {
            var payload = await AccountRecoveryPayloads.ReadAsync(context, codec, "LinkageCode", "Password");
            object?[] result;
            try
            {
                result = await state.AccountService.GetTakeOverAccountAsync(payload[0], payload[1]);
            }
            catch (BadHttpRequestException error) when (error.Message is AccountErrors.InvalidTakeOverCredentials or AccountErrors.RecoveryRateLimited)
            {
                // 引继凭据失败由客户端 IsSuccess 分支处理；HTTP 401 会使客户端回到标题并显示通用错误。
                await state.Logger.LogAsync($"level=WARN operation=account-takeover requestId={context.TraceIdentifier} errorCode={error.Message} outcome=failed");
                await codec.WriteApiResultAsync(context, new object?[] { false, "", "", 0, "" }, "five-frame");
                return;
            }
            await state.Logger.LogAsync($"level=INFO operation=account-takeover requestId={context.TraceIdentifier} outcome=success");
            await codec.WriteApiResultAsync(context, result, "five-frame");
        });
    }
}
