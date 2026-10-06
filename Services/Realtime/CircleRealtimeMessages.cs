using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using System.Text;

sealed partial class SocialRealtimeService
{
    static readonly Lazy<HashSet<long>> stamps = new(() =>
    {
        using var stream = typeof(SocialRealtimeService).Assembly.GetManifestResourceStream("Realtime.StampMaster.json")
            ?? throw new InvalidOperationException("缺少聊天表情主数据");
        using var table = JsonDocument.Parse(stream);
        return table.RootElement.EnumerateArray().Select(row => row[0].GetInt64()).ToHashSet();
    });
    static readonly Lazy<string[]> ngWords = new(() =>
    {
        using var stream = typeof(SocialRealtimeService).Assembly.GetManifestResourceStream("Realtime.NgWordMaster.json")
            ?? throw new InvalidOperationException("缺少聊天禁词主数据");
        using var table = JsonDocument.Parse(stream);
        return table.RootElement.EnumerateArray().Select(row => row[1].GetString()!.Normalize(NormalizationForm.FormKC)).ToArray();
    });
    static async Task<object?[]> ChatsAsync(NpgsqlConnection db, string circle, long? before)
    {
        await using var command = new NpgsqlCommand("""
            select id,payload,sent_at from circle_realtime_chats
            where circle_id=$1 and not deleted and ($2::bigint is null or id<$2) order by id desc limit 100
            """, db);
        command.Parameters.AddWithValue(circle);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)before ?? DBNull.Value });
        var rows = new List<object?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(Chat(reader.GetInt64(0), (byte[])reader[1], reader.GetDateTime(2)));
        rows.Reverse();
        return rows.ToArray();
    }
    static object?[] Chat(long id, byte[] payload, DateTime sent)
    {
        var chat = (object?[])MsgPack.Decode(payload)!;
        chat[0] = id; chat[4] = sent;
        return chat;
    }
    static async Task<object?[]> ProfileAsync(NpgsqlConnection db, long user, NpgsqlTransaction? tx = null)
    {
        await using var command = new NpgsqlCommand("""
            select a.public_id,coalesce(p.display_name,a.name),coalesce(c.character_master_id,p.favorite_character_master_id,110010),
              coalesce(b.portal_display_awakening_status,false),coalesce(p.icon_frame_master_id,190001),coalesce(g.rank,1),p.title_master_id
            from user_accounts a left join user_profiles p on p."userId"=a.id
            left join user_game_states g on g."userId"=a.id
            left join user_character_cards c on c.id=p.favorite_character_id and c."userId"=a.id
            left join user_character_bases b on b.id=c.character_base_id and b."userId"=a.id where a.id=$1
            """, db, tx);
        command.Parameters.AddWithValue(user);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("实时用户不存在");
        return new object?[] { reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetBoolean(3), reader.GetInt64(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt64(6) };
    }
    static async Task<string> LockMemberAsync(NpgsqlConnection db, NpgsqlTransaction tx, long user, string? expected)
    {
        await using var command = new NpgsqlCommand("select circle_id from user_circle_memberships where \"userId\"=$1 for update", db, tx);
        command.Parameters.AddWithValue(user);
        var circle = await command.ExecuteScalarAsync() as string;
        if (circle is null || circle != expected) throw Denied();
        return circle;
    }
    async Task<object?[]> SendChatAsync(Connection session, object? value, CancellationToken cancellation)
    {
        var args = MultiLiveWire.Args(value, 7);
        if (args[0] is not string message || args[2] is not string || args[4] is not bool
            || args[5] is not null and not string) throw MultiLiveWire.Invalid();
        MultiLiveWire.Number(args[3], 1); MultiLiveWire.Number(args[6], 1);
        var stamp = args[1] is null ? (long?)null : MultiLiveWire.Number(args[1], 1);
        if (message.Length > 140) return new object?[] { 2 };
        var normalized = message.Normalize(NormalizationForm.FormKC);
        if (ngWords.Value.Any(word => normalized.Contains(word, StringComparison.OrdinalIgnoreCase))) return new object?[] { 3 };
        if (stamp is not null && !stamps.Value.Contains(stamp.Value)) throw MultiLiveWire.Invalid();
        if (stamp is null && string.IsNullOrWhiteSpace(message)) throw MultiLiveWire.Invalid();
        if (message.StartsWith("HallId:", StringComparison.Ordinal)) throw MultiLiveWire.Invalid();
        await writes.WaitAsync(cancellation);
        try
        {
            await using var db = await database.OpenConnectionAsync(cancellation);
            await using var tx = await db.BeginTransactionAsync(cancellation);
            var circle = await LockMemberAsync(db, tx, session.UserId, session.CircleId);
            await using (var recent = new NpgsqlCommand("select exists(select 1 from circle_realtime_chats where sender_id=$1 and sent_at>now()-interval '10 seconds')", db, tx))
            {
                recent.Parameters.AddWithValue(session.UserId);
                if (await recent.ExecuteScalarAsync(cancellation) is true) return new object?[] { 1 };
            }
            var mention = args[5] as string;
            if (!string.IsNullOrEmpty(mention))
            {
                await using var member = new NpgsqlCommand("select exists(select 1 from user_circle_memberships m join user_accounts a on a.id=m.\"userId\" where m.circle_id=$1 and a.public_id=$2)", db, tx);
                member.Parameters.AddWithValue(circle); member.Parameters.AddWithValue(mention);
                if (await member.ExecuteScalarAsync(cancellation) is not true) throw MultiLiveWire.Invalid();
            }
            var profile = await ProfileAsync(db, session.UserId, tx);
            var chat = new object?[] { 0L, profile[0], message, stamp, DateTime.UtcNow, 0, profile[1], profile[2], profile[3], mention, profile[4] };
            var saved = await InsertChatAsync(db, tx, circle, session.UserId, chat, cancellation);
            await tx.CommitAsync(cancellation);
            await PublishAsync(db, circle, "OnReceiveChat", saved);
            return new object?[] { 0 };
        }
        finally { writes.Release(); }
    }
    static async Task<object?[]> InsertChatAsync(NpgsqlConnection db, NpgsqlTransaction tx, string circle, long user, object?[] chat, CancellationToken cancellation)
    {
        await using var insert = new NpgsqlCommand("insert into circle_realtime_chats(circle_id,sender_id,payload) values($1,$2,$3) returning id,sent_at", db, tx);
        insert.Parameters.AddWithValue(circle); insert.Parameters.AddWithValue(user); insert.Parameters.AddWithValue(MsgPack.Encode(chat));
        await using var reader = await insert.ExecuteReaderAsync(cancellation);
        if (!await reader.ReadAsync(cancellation)) throw new InvalidOperationException("聊天写入未返回编号");
        chat[0] = reader.GetInt64(0); chat[4] = reader.GetDateTime(1);
        return chat;
    }
    static async Task<object?[]> ReadChatAsync(NpgsqlConnection db, string circle, long user, NpgsqlTransaction? tx = null)
    {
        await using var command = new NpgsqlCommand("""
            select coalesce((select last_read_id from circle_realtime_reads where circle_id=$1 and user_id=$2),0),
              coalesce((select max(id) from circle_realtime_chats where circle_id=$1),0)
            """, db, tx);
        command.Parameters.AddWithValue(circle); command.Parameters.AddWithValue(user);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("已读查询未返回结果");
        return new object?[] { reader.GetInt64(0), reader.GetInt64(1) };
    }
    async Task SaveReadAsync(Connection session, long id, CancellationToken cancellation)
    {
        await writes.WaitAsync(cancellation);
        try
        {
            await using var db = await database.OpenConnectionAsync(cancellation);
            await using var tx = await db.BeginTransactionAsync(cancellation);
            var circle = await LockMemberAsync(db, tx, session.UserId, session.CircleId);
            await using var command = new NpgsqlCommand("""
                insert into circle_realtime_reads(circle_id,user_id,last_read_id)
                select $1,$2,$3 where $3=0 or exists(select 1 from circle_realtime_chats where circle_id=$1 and id=$3)
                on conflict(circle_id,user_id) do update set last_read_id=greatest(circle_realtime_reads.last_read_id,excluded.last_read_id)
                """, db, tx);
            command.Parameters.AddWithValue(circle); command.Parameters.AddWithValue(session.UserId); command.Parameters.AddWithValue(id);
            if (await command.ExecuteNonQueryAsync(cancellation) != 1) throw MultiLiveWire.Invalid();
            var read = await ReadChatAsync(db, circle, session.UserId, tx);
            await tx.CommitAsync(cancellation);
            var live = await LiveSessionsAsync(db);
            foreach (var other in connections.Keys.Where(s => s.Joined && s.Hub == "ICircleHub" && s.UserId == session.UserId && s.CircleId == circle && live.Contains(s.AuthSessionId)))
                other.Send(RealtimeHubProtocol.Broadcast("OnReceiveReadChat", read));
        }
        finally { writes.Release(); }
    }
    async Task DeleteChatAsync(Connection session, long id, CancellationToken cancellation)
    {
        await writes.WaitAsync(cancellation);
        try
        {
            await using var db = await database.OpenConnectionAsync(cancellation);
            await using var tx = await db.BeginTransactionAsync(cancellation);
            var circle = await LockMemberAsync(db, tx, session.UserId, session.CircleId);
            // 删除仅允许消息作者，避免客户端传入任意聊天编号越权。
            await using var command = new NpgsqlCommand("update circle_realtime_chats set deleted=true where circle_id=$1 and id=$2 and sender_id=$3", db, tx);
            command.Parameters.AddWithValue(circle); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(session.UserId);
            if (await command.ExecuteNonQueryAsync(cancellation) != 1) throw Denied();
            await tx.CommitAsync(cancellation);
            await PublishAsync(db, circle, "OnDeleteChat", id);
        }
        finally { writes.Release(); }
    }
    static async Task<object?[]> ActivitiesAsync(NpgsqlConnection db, string circle)
    {
        await using var command = new NpgsqlCommand("select public_id,user_name,character_id,log_type,log_value,id,logged_at,display_awakening from circle_realtime_activities where circle_id=$1 order by id desc limit 100", db);
        command.Parameters.AddWithValue(circle);
        var rows = new List<object?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(new object?[] { reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetBoolean(7), reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5), reader.GetDateTime(6) });
        rows.Reverse(); return rows.ToArray();
    }
}
