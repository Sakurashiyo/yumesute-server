using Grpc.Core;
using Npgsql;

sealed partial class RealtimeHubService(LocalServerState state)
{
    public async Task ConnectAsync(string hub, IAsyncStreamReader<byte[]> requests, IServerStreamWriter<byte[]> responses, ServerCallContext call)
    {
        var authenticated = await AuthenticateAsync(call);
        var userId = authenticated.UserId;
        var logger = call.GetHttpContext().RequestServices.GetRequiredService<ILogger<RealtimeHubService>>();
        logger.LogInformation("实时连接建立 hub={Hub} userId={UserId} connectionId={ConnectionId}", hub, userId, call.GetHttpContext().TraceIdentifier);
        await call.WriteResponseHeadersAsync(new Metadata { { "x-magiconion-streaminghub-version", "2" } });
        // MagicOnion 客户端等待首帧才完成 ConnectAsync；-1 请求编号是协议就绪标记。
        await responses.WriteAsync(RealtimeHubProtocol.Response(-1, 0, null));
        if (hub == "IMultiLiveHub")
        {
            await RunMultiLiveStreamAsync(authenticated.Identity, requests, responses, call, logger);
            return;
        }
        await RunSocialStreamAsync(userId, authenticated.Identity, authenticated.SessionId, hub, requests, responses, call, logger);
    }

    async Task<(long UserId, string Identity, long SessionId)> AuthenticateAsync(ServerCallContext call)
    {
        var headers = call.GetHttpContext().Request.Headers;
        var token = headers.Authorization.ToString();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            foreach (var name in new[] { "x-api-token", "api-token", "apitoken", "token" })
            {
                token = headers[name].ToString();
                if (!string.IsNullOrEmpty(token)) break;
            }
        }
        if (string.IsNullOrEmpty(token)) throw new RpcException(new Status(StatusCode.Unauthenticated, RealtimeHubProtocol.AuthRequired));
        await using var connection = await state.Database.OpenConnectionAsync(call.CancellationToken);
        await using var command = new NpgsqlCommand("""
            select s."userId", a.public_id, s.id from user_auth_sessions s join user_accounts a on a.id=s."userId"
            where s.token_hash=$1 and s.expires_at>now() order by s.id desc limit 1
            """, connection);
        command.Parameters.AddWithValue(CryptoService.Sha256(token));
        await using var reader = await command.ExecuteReaderAsync(call.CancellationToken);
        if (!await reader.ReadAsync(call.CancellationToken)) throw new RpcException(new Status(StatusCode.Unauthenticated, RealtimeHubProtocol.AuthInvalid));
        // 与 HTTP 用户数据中的 public_id 一致，身份不能由客户端的加入参数指定。
        return (reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2));
    }
}
