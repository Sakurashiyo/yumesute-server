using Grpc.Core;
using Npgsql;

sealed class RealtimeHubService(LocalServerState state)
{
    public async Task ConnectAsync(string hub, IAsyncStreamReader<byte[]> requests, IServerStreamWriter<byte[]> responses, ServerCallContext call)
    {
        var userId = await AuthenticateAsync(call);
        var logger = call.GetHttpContext().RequestServices.GetRequiredService<ILogger<RealtimeHubService>>();
        logger.LogInformation("实时连接建立 hub={Hub} userId={UserId} connectionId={ConnectionId}", hub, userId, call.GetHttpContext().TraceIdentifier);
        await call.WriteResponseHeadersAsync(new Metadata { { "x-magiconion-streaminghub-version", "2" } });
        // MagicOnion 客户端等待首帧才完成 ConnectAsync；-1 请求编号是协议就绪标记。
        await responses.WriteAsync(RealtimeHubProtocol.Response(-1, 0, null));
        var joined = false;
        try
        {
            while (await requests.MoveNext(call.CancellationToken))
            {
                var request = RealtimeHubProtocol.ReadRequest(requests.Current);
                if (request.MethodId == RealtimeHubProtocol.MethodId("JoinAsync"))
                {
                    if (!joined)
                    {
                        joined = true;
                        await responses.WriteAsync(RealtimeHubProtocol.Broadcast("OnJoin", null));
                        logger.LogInformation("实时加入完成 hub={Hub} userId={UserId}", hub, userId);
                    }
                    if (request.MessageId >= 0)
                        await responses.WriteAsync(RealtimeHubProtocol.Response(request.MessageId, request.MethodId, null));
                    continue;
                }
                if (!joined)
                {
                    if (request.MessageId >= 0)
                        await responses.WriteAsync(RealtimeHubProtocol.Error(request.MessageId, StatusCode.FailedPrecondition, RealtimeHubProtocol.NotJoined));
                    continue;
                }
                var supportedQuery = hub == "ICommonHub"
                    ? request.MethodId == RealtimeHubProtocol.MethodId("GetMultiLiveInvitationsFromFriendAsync")
                    : request.MethodId == RealtimeHubProtocol.MethodId("GetChatsAsync") || request.MethodId == RealtimeHubProtocol.MethodId("GetActivityLogsAsync");
                if (request.MessageId >= 0)
                {
                    // 本地实时兼容阶段没有存储聊天或邀请；未支持的写入必须明确失败。
                    await responses.WriteAsync(supportedQuery
                        ? RealtimeHubProtocol.Response(request.MessageId, request.MethodId, Array.Empty<object?>())
                        : RealtimeHubProtocol.Error(request.MessageId, StatusCode.Unimplemented, RealtimeHubProtocol.MethodUnimplemented));
                }
                if (!supportedQuery)
                    logger.LogWarning("实时方法未实现 hub={Hub} methodId={MethodId} userId={UserId}", hub, request.MethodId, userId);
            }
        }
        catch (OperationCanceledException) when (call.CancellationToken.IsCancellationRequested)
        {
            // 客户端退出或切换场景会取消连接，这是连接生命周期的一部分。
            logger.LogDebug("实时连接取消 hub={Hub} userId={UserId}", hub, userId);
        }
        finally
        {
            logger.LogInformation("实时连接关闭 hub={Hub} userId={UserId}", hub, userId);
        }
    }

    async Task<long> AuthenticateAsync(ServerCallContext call)
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
        await using var command = new NpgsqlCommand("select \"userId\" from user_auth_sessions where token_hash = $1 and expires_at > now() order by id desc limit 1", connection);
        command.Parameters.AddWithValue(CryptoService.Sha256(token));
        var userId = await command.ExecuteScalarAsync(call.CancellationToken);
        return userId is long id ? id : throw new RpcException(new Status(StatusCode.Unauthenticated, RealtimeHubProtocol.AuthInvalid));
    }
}
