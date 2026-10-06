using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;
using Npgsql;

sealed partial class SocialRealtimeService(PostgresDatabase database, MultiLiveRealtimeService multiLive)
{
    // 社交通知与大厅消息都复用有界队列，慢连接断开后从数据库恢复历史。
    internal sealed class Connection(long userId, string identity, string hub, long authSessionId)
    {
        public long UserId { get; } = userId;
        public string Identity { get; } = identity;
        public string Hub { get; } = hub;
        public long AuthSessionId { get; } = authSessionId;
        public bool Joined { get; set; }
        public string? CircleId { get; set; }
        public long LastActivity { get; set; }
        public CancellationTokenSource Stop { get; } = new();
        public Channel<byte[]> Outgoing { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(128) { SingleReader = true });
        readonly object lifecycle = new();
        bool disposed;
        public void Cancel() { lock (lifecycle) { if (!disposed) Stop.Cancel(); } }
        public void Dispose() { lock (lifecycle) { disposed = true; Stop.Dispose(); } }
        public void Send(byte[] frame) { if (!Outgoing.Writer.TryWrite(frame)) Cancel(); }
    }
    readonly ConcurrentDictionary<Connection, byte> connections = new();
    // 保证同一进程的提交与广播顺序一致；数据库行锁同时保护跨连接限频与已读更新。
    readonly SemaphoreSlim writes = new(1, 1);
    internal Connection Attach(long userId, string identity, string hub, long authSessionId)
    {
        var session = new Connection(userId, identity, hub, authSessionId);
        connections.TryAdd(session, 0);
        return session;
    }
    internal void Detach(Connection session)
    {
        connections.TryRemove(session, out _);
        session.Outgoing.Writer.TryComplete();
    }
    static RpcException Denied() => new(new Status(StatusCode.PermissionDenied, RealtimeHubProtocol.CirclePermissionDenied));
    internal async Task DispatchAsync(Connection session, byte[] bytes, ILogger logger, CancellationToken cancellation)
    {
        var request = RealtimeHubProtocol.ReadRequest(bytes);
        try
        {
            var args = RealtimeHubProtocol.ReadArguments(bytes);
            var result = await HandleAsync(session, request.MethodId, args, cancellation);
            if (request.MessageId >= 0) session.Send(RealtimeHubProtocol.Response(request.MessageId, request.MethodId, result));
            if (request.MethodId == RealtimeHubProtocol.MethodId("TrySendChatAsync"))
            {
                var code = Convert.ToInt32(((object?[])result!)[0]);
                if (code == 0) logger.LogInformation("社团聊天发送 userId={UserId} circleId={CircleId}", session.UserId, session.CircleId);
                else logger.LogWarning("社团聊天拒绝 userId={UserId} errorCode={ErrorCode}", session.UserId, code);
            }
            else if (request.MethodId == RealtimeHubProtocol.MethodId("DeleteChatAsync") || request.MethodId == RealtimeHubProtocol.MethodId("SaveReadChatAsync"))
                logger.LogInformation("社团消息状态更新 userId={UserId} methodId={MethodId}", session.UserId, request.MethodId);
        }
        catch (RpcException error)
        {
            if (request.MessageId >= 0) session.Send(RealtimeHubProtocol.Error(request.MessageId, error.StatusCode, error.Status.Detail));
            logger.LogWarning("社交实时请求拒绝 hub={Hub} userId={UserId} methodId={MethodId} errorCode={ErrorCode}", session.Hub, session.UserId, request.MethodId, error.Status.Detail);
        }
    }
    async Task<object?> HandleAsync(Connection session, int method, object? args, CancellationToken cancellation)
    {
        bool Is(string name) => method == RealtimeHubProtocol.MethodId(name);
        if (!Is("JoinAsync"))
        {
            if (!session.Joined && Is("TrySendChatAsync")) return new object?[] { 4 };
            if (!session.Joined) throw new RpcException(new Status(StatusCode.FailedPrecondition, RealtimeHubProtocol.NotJoined));
            // 排队等待写锁时不占用读连接，避免并发请求耗尽连接池后无法取得写连接。
            if (session.Hub == "ICircleHub")
            {
                if (Is("TrySendChatAsync"))
                {
                    try { return await SendChatAsync(session, args, cancellation); }
                    catch (RpcException error) when (error.Status.Detail == RealtimeHubProtocol.CirclePermissionDenied)
                    { return new object?[] { 4 }; }
                }

                if (Is("SaveReadChatAsync")) { await SaveReadAsync(session, MultiLiveWire.Number(args), cancellation); return null; }
                if (Is("DeleteChatAsync")) { await DeleteChatAsync(session, MultiLiveWire.Number(args, 1), cancellation); return null; }
            }
        }
        await using var db = await database.OpenConnectionAsync(cancellation);
        if (Is("JoinAsync"))
        {
            if (args is not null) throw MultiLiveWire.Invalid();
            if (session.Hub == "ICircleHub")
            {
                session.CircleId = await CircleAsync(db, session.UserId);
                session.Send(RealtimeHubProtocol.Broadcast("OnJoinStatus", new object?[] { session.CircleId is null ? 2 : 1 }));
                if (session.CircleId is null) { session.Joined = false; return null; }
                var activities = await ActivitiesAsync(db, session.CircleId);
                session.LastActivity = activities.Length == 0 ? 0 : Convert.ToInt64(((object?[])activities[^1]!)[6]);
            }
            if (!session.Joined) session.Send(RealtimeHubProtocol.Broadcast("OnJoin", null));
            session.Joined = true;
            return null;
        }
        if (session.Hub == "ICommonHub")
        {
            if (Is("GetMultiLiveInvitationsFromFriendAsync") && args is null) return await InvitationsAsync(db, session.UserId);
            throw new RpcException(new Status(StatusCode.Unimplemented, RealtimeHubProtocol.MethodUnimplemented));
        }
        var circle = await CircleAsync(db, session.UserId);
        if (circle is null || circle != session.CircleId)
        {
            if (Is("TrySendChatAsync")) return new object?[] { 4 };
            throw Denied();
        }
        if (Is("GetChatsAsync")) return await ChatsAsync(db, circle, args is null ? null : MultiLiveWire.Number(args, 1));
        if (Is("GetReadChatAsync") && args is null) return await ReadChatAsync(db, circle, session.UserId);
        if (Is("GetActivityLogsAsync") && args is null) return await ActivitiesAsync(db, circle);

        throw new RpcException(new Status(StatusCode.Unimplemented, RealtimeHubProtocol.MethodUnimplemented));
    }
    static async Task<string?> CircleAsync(NpgsqlConnection db, long user, NpgsqlTransaction? tx = null)
    {
        await using var command = new NpgsqlCommand("select circle_id from user_circle_memberships where \"userId\"=$1", db, tx);
        command.Parameters.AddWithValue(user);
        return await command.ExecuteScalarAsync() as string;
    }
    async Task<HashSet<long>> LiveSessionsAsync(NpgsqlConnection db)
    {
        var active = connections.Keys.Where(s => s.Joined).ToArray();
        var live = new HashSet<long>();
        await using var command = new NpgsqlCommand("select id from user_auth_sessions where id=any($1) and expires_at>now()", db);
        command.Parameters.AddWithValue(active.Select(s => s.AuthSessionId).Distinct().ToArray());
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) live.Add(reader.GetInt64(0));
        // 引继或会话撤销后，旧连接不能继续收到跨连接推送。
        foreach (var session in active)
            if (!live.Contains(session.AuthSessionId)) session.Cancel();
        return live;
    }
    async Task PublishAsync(NpgsqlConnection db, string circle, string method, object? value)
    {
        var members = new HashSet<long>();
        await using (var command = new NpgsqlCommand("select \"userId\" from user_circle_memberships where circle_id=$1", db))
        {
            command.Parameters.AddWithValue(circle);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) members.Add(reader.GetInt64(0));
        }
        var live = await LiveSessionsAsync(db);
        var frame = RealtimeHubProtocol.Broadcast(method, value);
        foreach (var session in connections.Keys)
            if (session.Joined && session.Hub == "ICircleHub" && session.CircleId == circle && members.Contains(session.UserId) && live.Contains(session.AuthSessionId)) session.Send(frame);
    }
    internal async Task PublishActivitiesAsync()
    {
        await writes.WaitAsync();
        try
        {
            await using var db = await database.OpenConnectionAsync();
            var live = await LiveSessionsAsync(db);
            foreach (var session in connections.Keys.Where(s => s.Joined && s.Hub == "ICircleHub" && live.Contains(s.AuthSessionId)))
            {
                if (await CircleAsync(db, session.UserId) != session.CircleId) continue;
                foreach (var activity in (await ActivitiesAsync(db, session.CircleId!)).Cast<object?[]>())
                {
                    var id = Convert.ToInt64(activity[6]);
                    if (id <= session.LastActivity) continue;
                    session.Send(RealtimeHubProtocol.Broadcast("OnReceiveActivityLog", activity));
                    session.LastActivity = id;
                }
            }
        }
        finally { writes.Release(); }
    }
}
