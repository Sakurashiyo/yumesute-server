using Grpc.Core;
using Npgsql;

sealed partial class SocialRealtimeService
{
    internal async Task NotifyInvitationsAsync(long user, string identity, bool circleInvite, object? value, CancellationToken cancellation)
    {
        var args = MultiLiveWire.Args(value, circleInvite ? 3 : 8);
        var offset = circleInvite ? 0 : 1;
        MultiLiveWire.Number(args[offset], 1); MultiLiveWire.Boolean(args[offset + 1]);
        if (!circleInvite)
        {
            MultiLiveWire.Number(args[3], 1, int.MaxValue);
            for (var i = 4; i <= 6; i++) if (args[i] is not null) MultiLiveWire.Number(args[i], 1);
        }
        MultiLiveWire.Number(args[circleInvite ? 2 : 7], 1);
        string[] targets = Array.Empty<string>();
        if (!circleInvite)
        {
            var ids = MultiLiveWire.Args(args[0], (args[0] as object?[])?.Length ?? -1);
            if (ids.Length is < 1 or > 100) throw MultiLiveWire.Invalid();
            targets = ids.Select(id => MultiLiveWire.Text(id, 128)).Distinct(StringComparer.Ordinal).ToArray();
        }
        await writes.WaitAsync(cancellation);
        try
        {
            var room = multiLive.RecruitmentFor(identity);
            if (room is null || room.HostIdentity != identity) throw new RpcException(new Status(StatusCode.FailedPrecondition, RealtimeHubProtocol.InviteRoomUnavailable));
            await using var db = await database.OpenConnectionAsync(cancellation);
            await using var tx = await db.BeginTransactionAsync(cancellation);
            var profile = await ProfileAsync(db, user, tx);
            if (circleInvite)
            {
                var circle = await CircleAsync(db, user, tx);
                if (circle is null) throw Denied();
                await LockMemberAsync(db, tx, user, circle);
                var message = $"HallId:{room.HallId}/Name:{profile[1]}/Count:{room.Count}/MultiLiveType:{room.Type}";
                // 招募消息由服务器生成，客户端普通聊天不能伪造房间或发起者。
                var chat = new object?[] { 0L, identity, message, null, DateTime.UtcNow, 4, profile[1], profile[2], profile[3], null, profile[4] };
                await using var recent = new NpgsqlCommand("select exists(select 1 from circle_realtime_chats where sender_id=$1 and sent_at>now()-interval '10 seconds')", db, tx);
                recent.Parameters.AddWithValue(user);
                if (await recent.ExecuteScalarAsync(cancellation) is true) throw new RpcException(new Status(StatusCode.ResourceExhausted, RealtimeHubProtocol.InviteTooFrequent));
                await InsertChatAsync(db, tx, circle, user, chat, cancellation);
                await tx.CommitAsync(cancellation);
                await PublishAsync(db, circle, "OnReceiveChat", chat);
                await PublishAsync(db, circle, "OnNotifyMultiLiveRequest", new object?[] { room.Type, room.HallId, profile[1] });
            }
            else
            {
                var recipients = new List<long>();
                await using (var query = new NpgsqlCommand("""
                    select a.id,a.public_id from user_friend_relationships f join user_accounts a on a.id=f."friendUserId"
                    where f."userId"=$1 and a.public_id=any($2) and a.id<>$1
                    and not exists(select 1 from user_friend_blocks b where
                      (b."userId"=$1 and b."blockedUserId"=a.id) or (b."userId"=a.id and b."blockedUserId"=$1))
                    """, db, tx))
                {
                    query.Parameters.AddWithValue(user); query.Parameters.AddWithValue(targets);
                    await using var reader = await query.ExecuteReaderAsync(cancellation);
                    while (await reader.ReadAsync(cancellation)) recipients.Add(reader.GetInt64(0));
                }
                if (recipients.Count != targets.Length) throw new RpcException(new Status(StatusCode.PermissionDenied, RealtimeHubProtocol.InvitePermissionDenied));
                var notified = new HashSet<long>();
                foreach (var recipient in recipients)
                {
                    var invite = new object?[] { Guid.NewGuid().ToString("N"), profile[1], profile[2], profile[3], profile[5], profile[6], null, null, room.Type, room.HallId, room.Count, DateTime.UtcNow, profile[4] };
                    await using var save = new NpgsqlCommand("""
                        insert into realtime_friend_invitations(recipient_id,sender_id,hall_id,payload) values($1,$2,$3,$4)
                        on conflict(recipient_id,sender_id,hall_id) do update set payload=excluded.payload,invited_at=now()
                        where realtime_friend_invitations.invited_at<now()-interval '10 seconds'
                        """, db, tx);
                    save.Parameters.AddWithValue(recipient); save.Parameters.AddWithValue(user); save.Parameters.AddWithValue(room.HallId); save.Parameters.AddWithValue(MsgPack.Encode(invite));
                    if (await save.ExecuteNonQueryAsync(cancellation) == 1) notified.Add(recipient);
                }
                await tx.CommitAsync(cancellation);
                var live = await LiveSessionsAsync(db);
                var frame = RealtimeHubProtocol.Broadcast("OnNotifyInviteMultiLiveFromFriend", new object?[] { room.Type, room.HallId, profile[1] });
                foreach (var session in connections.Keys.Where(s => s.Joined && s.Hub == "ICommonHub" && notified.Contains(s.UserId) && live.Contains(s.AuthSessionId))) session.Send(frame);
            }
        }
        finally { writes.Release(); }
    }
    async Task<object?[]> InvitationsAsync(NpgsqlConnection db, long user)
    {
        // 超时记录清理后仍需核验大厅，重启、房间解散或开始演出后的邀请不会继续显示。
        await using (var cleanup = new NpgsqlCommand("delete from realtime_friend_invitations where invited_at<=now()-interval '10 minutes'", db))
            await cleanup.ExecuteNonQueryAsync();
        await using var query = new NpgsqlCommand("""
            select i.payload,a.public_id from realtime_friend_invitations i join user_accounts a on a.id=i.sender_id
            where i.recipient_id=$1 and exists(select 1 from user_friend_relationships f where f."userId"=$1 and f."friendUserId"=i.sender_id)
            and exists(select 1 from user_friend_relationships f where f."userId"=i.sender_id and f."friendUserId"=$1)
            and not exists(select 1 from user_friend_blocks b where
              (b."userId"=$1 and b."blockedUserId"=i.sender_id) or (b."userId"=i.sender_id and b."blockedUserId"=$1))
            order by i.invited_at desc limit 100
            """, db);
        query.Parameters.AddWithValue(user);
        var rows = new List<object?>();
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var invite = (object?[])MsgPack.Decode((byte[])reader[0])!;
            var room = multiLive.RecruitmentFor(reader.GetString(1));
            if (room is null || room.HostIdentity != reader.GetString(1) || room.HallId != (string)invite[9]!) continue;
            invite[10] = room.Count; rows.Add(invite);
        }
        return rows.ToArray();
    }
    internal async Task NotifyFriendRequestAsync(long sender, long recipient)
    {
        await using var db = await database.OpenConnectionAsync();
        var circle = await CircleAsync(db, sender);
        if (circle is null || circle != await CircleAsync(db, recipient)) return;
        var profile = await ProfileAsync(db, sender);
        var live = await LiveSessionsAsync(db);
        var frame = RealtimeHubProtocol.Broadcast("OnNotifyFriendRequest", new object?[] { profile[0], profile[1] });
        foreach (var session in connections.Keys.Where(s => s.Joined && s.Hub == "ICircleHub" && s.UserId == recipient && s.CircleId == circle && live.Contains(s.AuthSessionId)))
            session.Send(frame);
    }
}
