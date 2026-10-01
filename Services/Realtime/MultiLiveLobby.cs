using Grpc.Core;
using SiriusLocalServer.Realtime;
using System.Threading.Channels;

sealed partial class MultiLiveRealtimeService
{
    readonly Dictionary<string, LobbyConnection> connections = new(StringComparer.Ordinal);
    readonly Dictionary<string, DateTimeOffset> disconnectedAt = new(StringComparer.Ordinal);
    static readonly Dictionary<int, string> LobbyMethods = new[] { "CreatePrivateHallAsync", "JoinPublicHallAsync",
        "JoinPrivateHallWithKeyCodeAsync", "JoinPrivateHallFromInviteAsync", "FetchUsersAsync",
        "ReadyForDecideMember", "SelectMusicAsync", "SelectDifficultyAsync", "SelectStampAsync" }
        .ToDictionary(RealtimeHubProtocol.MethodId);

    internal sealed class LobbyConnection(string identity)
    {
        public string Identity { get; } = identity;
        public CancellationTokenSource Stop { get; } = new();
        public Channel<byte[]> Outgoing { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(128)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        public void Send(byte[] frame)
        {
            if (!Outgoing.Writer.TryWrite(frame)) Stop.Cancel();
        }
    }

    internal LobbyConnection Attach(string identity)
    {
        lock (SyncRoot)
        {
            PruneDisconnected();
            if (connections.Remove(identity, out var previous)) previous.Stop.Cancel();
            var session = new LobbyConnection(identity);
            connections.Add(identity, session);
            disconnectedAt.Remove(identity);
            return session;
        }
    }
    internal void Disconnect(LobbyConnection session)
    {
        lock (SyncRoot)
        {
            // 被新连接取代的旧流不能移除新流或重复释放席位。
            if (connections.GetValueOrDefault(session.Identity) == session)
            {
                connections.Remove(session.Identity);
                disconnectedAt[session.Identity] = DateTimeOffset.UtcNow;
            }
            session.Outgoing.Writer.TryComplete();
        }
    }
    void PruneDisconnected()
    {
        foreach (var identity in disconnectedAt.Where(p => p.Value < DateTimeOffset.UtcNow.AddMinutes(-5)).Select(p => p.Key).ToArray())
        {
            LeaveRoom(identity);
            disconnectedAt.Remove(identity);
        }
    }
    void Broadcast(MultiLiveRoomState room, string method, object? value, string? except = null)
    {
        var frame = RealtimeHubProtocol.Broadcast(method, value);
        foreach (var member in room.Users.Values)
            if (member.HashUserId != except && connections.TryGetValue(member.HashUserId, out var session)) session.Send(frame);
    }
    void LeaveRoom(string identity)
    {
        var room = GetRoomForUser(identity);
        if (room is null) return;
        var member = room.Users.Values.Single(u => u.HashUserId == identity);
        room.Users.Remove(member.MemberId);
        hallIdByHashUserId.TryRemove(identity, out _);
        if (room.Users.Count == 0)
        {
            roomsByHallId.TryRemove(room.HallId, out _);
            hallIdByMultiLiveId.TryRemove(room.MultiLiveId, out _);
        }
        else
        {
            if (room.HostMemberId == member.MemberId) room.HostMemberId = room.Users.Keys.Min();
            Broadcast(room, "OnLeaveAnyOne", member.MemberId);
        }
    }

    internal void DispatchLobby(LobbyConnection session, byte[] payload, ILogger logger)
    {
        var request = RealtimeHubProtocol.ReadRequest(payload);
        lock (SyncRoot)
        {
            if (connections.GetValueOrDefault(session.Identity) != session || session.Stop.IsCancellationRequested) return;
            PruneDisconnected();
            try
            {
                if (!LobbyMethods.TryGetValue(request.MethodId, out var method))
                    throw new RpcException(new Status(StatusCode.Unimplemented, RealtimeHubProtocol.MethodUnimplemented));
                var args = RealtimeHubProtocol.ReadArguments(payload);
                var result = HandleLobby(session.Identity, method, args);
                if (request.MessageId >= 0) session.Send(RealtimeHubProtocol.Response(request.MessageId, request.MethodId, result));
                if (method != "FetchUsersAsync") logger.LogInformation(
                    "实时大厅操作 operation={Operation} identity={Identity} hallId={HallId}", method, session.Identity, GetRoomForUser(session.Identity)?.HallId);
            }
            catch (RpcException error)
            {
                if (request.MessageId >= 0) session.Send(RealtimeHubProtocol.Error(request.MessageId, error.StatusCode, error.Status.Detail));
                logger.LogWarning("实时大厅请求拒绝 methodId={MethodId} errorCode={ErrorCode}", request.MethodId, error.Status.Detail);
            }
        }
    }

    object? HandleLobby(string identity, string method, object? args)
    {
        if (method == "CreatePrivateHallAsync")
        {
            var a = MultiLiveWire.Args(args, 5);
            var hall = MultiLiveWire.Hall(a[0]);
            if (MultiLiveWire.Hall(a[1]) != hall) throw MultiLiveWire.Invalid();
            var name = MultiLiveWire.Text(a[2]); var color = MultiLiveWire.Number(a[3]); var character = MultiLiveWire.ReadCharacter(a[4]);
            var previous = GetRoomForUser(identity);
            if (previous is { IsPrivate: true } && previous.LiveSettingMasterId == (long)hall
                && previous.Users[previous.HostMemberId].HashUserId == identity)
            {
                var seat = previous.Users.Values.Single(u => u.HashUserId == identity);
                return new object?[] { true, previous.HallId, seat.MemberId, previous.KeyCode, null };
            }
            LeaveRoom(identity);
            return MultiLiveWire.Created(CreatePrivateHallAsync(identity, hall, (long)hall, null, name, color, character).GetAwaiter().GetResult());
        }
        if (method.StartsWith("Join", StringComparison.Ordinal))
        {
            var a = MultiLiveWire.Args(args, 4);
            var name = MultiLiveWire.Text(a[1]); var color = MultiLiveWire.Number(a[2]); var character = MultiLiveWire.ReadCharacter(a[3]);
            MultiLiveRoomState? target;
            var isPublic = method == "JoinPublicHallAsync";
            var setting = isPublic ? (long)MultiLiveWire.Hall(a[0]) : 0;
            if (isPublic) target = roomsByHallId.Values.OrderByDescending(r => r.Users.Values.Any(u => u.HashUserId == identity)).FirstOrDefault(r => !r.IsPrivate && r.LiveSettingMasterId == setting
                && r.Status == MultiLiveHallStatus.Recruiting && (r.Users.Count < 4 || r.Users.Values.Any(u => u.HashUserId == identity)));
            else if (method == "JoinPrivateHallWithKeyCodeAsync")
            {
                var code = MultiLiveWire.Number(a[0], 100000, 999999);
                target = roomsByHallId.Values.SingleOrDefault(r => r.IsPrivate && r.KeyCode == code);
            }
            else target = roomsByHallId.GetValueOrDefault(MultiLiveWire.Text(a[0]));
            if (!isPublic && target is not { IsPrivate: true }) return MultiLiveWire.Join(MultiLiveJoinResult.Error(MultiLiveJoinErrorCodes.NotFoundHall));
            if (target is not null && target.Status != MultiLiveHallStatus.Recruiting)
                return MultiLiveWire.Join(MultiLiveJoinResult.Error(MultiLiveJoinErrorCodes.RoomClosed));
            var existed = target?.Users.Values.Any(u => u.HashUserId == identity) == true;
            if (target is not null && !existed && target.Users.Count >= 4)
                return MultiLiveWire.Join(MultiLiveJoinResult.Error(MultiLiveJoinErrorCodes.ReachedMaxMember));
            if (GetRoomForUser(identity) != target) LeaveRoom(identity);
            var joined = isPublic ? JoinPublicHallAsync(identity, (MultiLiveHallType)setting, setting, null, name, color, character).GetAwaiter().GetResult()
                : JoinPrivateHallFromInviteAsync(identity, null, target!.HallId, name, color, character).GetAwaiter().GetResult();
            var room = GetRoomForUser(identity) ?? throw new InvalidOperationException("加入成功后缺少房间");
            var member = room.Users[joined.MemberId];
            member.UserName = name; member.UserNamePlateColorId = color; member.LeaderCharacter = character;
            if (!existed) Broadcast(room, "OnJoin", MultiLiveWire.User(member), identity);
            return MultiLiveWire.Join(joined, isPublic);
        }
        var current = GetRoomForUser(identity) ?? throw new RpcException(new Status(StatusCode.FailedPrecondition, RealtimeHubProtocol.NotJoined));
        var user = current.Users.Values.Single(u => u.HashUserId == identity);
        if (method == "FetchUsersAsync")
        {
            if (args is not null) throw MultiLiveWire.Invalid();
            return MultiLiveWire.Users(current.ToFetchUsersResult());
        }
        if (method == "ReadyForDecideMember")
        {
            user.IsReadyDecideMember = MultiLiveWire.Boolean(args);
            Broadcast(current, "OnReadyDecideMember", new object?[] { user.MemberId, user.IsReadyDecideMember });
        }
        else if (method == "SelectMusicAsync")
        {
            var a = MultiLiveWire.Args(args, 3);
            var music = MultiLiveWire.Number(a[0]); var random = MultiLiveWire.Boolean(a[1]); var afk = MultiLiveWire.Boolean(a[2]);
            if (!random && !PlayerProgressionRules.Charts.Values.Any(c => c.MusicMasterId == music)) throw MultiLiveWire.Invalid();
            SelectMusicAsync(identity, music, random, afk).GetAwaiter().GetResult();
            Broadcast(current, "OnSelectMusic", new object?[] { user.MemberId, music, random });
        }
        else if (method == "SelectDifficultyAsync")
        {
            var a = MultiLiveWire.Args(args, 2);
            var difficulty = (MusicDifficulties)MultiLiveWire.Number(a[0], 1, 5); var afk = MultiLiveWire.Boolean(a[1]);
            SelectDifficultyAsync(identity, difficulty, afk).GetAwaiter().GetResult();
            Broadcast(current, "OnSelectDifficulty", new object?[] { user.MemberId, (int)difficulty });
        }
        else if (method == "SelectStampAsync")
            Broadcast(current, "OnSelectStamp", new object?[] { user.MemberId, MultiLiveWire.Number(args, 1) });
        return null;
    }
}
