using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> GetFriendListResultAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return new object?[] { Array.Empty<object?>(), 0 };

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        var rows = new List<(long FriendUserId, bool IsFavorite)>();
        await using (var command = new NpgsqlCommand(
            """
            select "friendUserId", is_favorite
            from user_friend_relationships
            where "userId" = $1
            order by is_favorite desc, created_at desc
            """,
            connection))
        {
            command.Parameters.AddWithValue(userId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetInt64(0), reader.GetBoolean(1)));
            }
        }

        var friends = new List<object?[]>();
        foreach (var row in rows)
        {
            var friend = await ReadFriendResultByUserIdAsync(connection, row.FriendUserId, row.IsFavorite);
            if (friend is not null) friends.Add(friend);
        }

        return new object?[] { friends.ToArray(), friends.Count };
    }

    public async Task<object?[]> GetBlockListResultAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return new object?[] { Array.Empty<object?>() };

        await using var connection = await database.OpenConnectionAsync();
        var blockedUserIds = new List<long>();
        await using (var command = new NpgsqlCommand(
            """
            select "blockedUserId"
            from user_friend_blocks
            where "userId" = $1
            order by created_at desc
            """,
            connection))
        {
            command.Parameters.AddWithValue(userId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                blockedUserIds.Add(reader.GetInt64(0));
            }
        }

        var users = new List<object?[]>();
        foreach (var blockedUserId in blockedUserIds)
        {
            var user = await ReadFriendResultByUserIdAsync(connection, blockedUserId, false);
            if (user is not null) users.Add(user);
        }

        return new object?[] { users.ToArray() };
    }

    public async Task<object?[]> GetFriendSearchResultAsync(HttpContext context, string? targetUserId)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            return new object?[] { null, 3 };
        }

        var friend = await ReadFriendResultByPublicIdAsync(targetUserId.Trim());
        return friend is null
            ? new object?[] { null, 3 }
            : new object?[] { friend, 4 };
    }

    public async Task<object?[]> GetOutgoingFriendRequestsAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return new object?[] { Array.Empty<object?>(), 0 };

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        var targetUserIds = new List<long>();
        await using (var command = new NpgsqlCommand(
            """
            select "toUserId"
            from user_friend_requests
            where "fromUserId" = $1
              and status = 'pending'
            order by requested_at desc
            """,
            connection))
        {
            command.Parameters.AddWithValue(userId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                targetUserIds.Add(reader.GetInt64(0));
            }
        }

        return new object?[] { await ReadFriendResultListAsync(connection, targetUserIds), targetUserIds.Count };
    }

    public async Task<object?[]> GetIncomingFriendRequestsAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return new object?[] { Array.Empty<object?>(), 0 };

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        var fromUserIds = new List<long>();
        await using (var command = new NpgsqlCommand(
            """
            select "fromUserId"
            from user_friend_requests
            where "toUserId" = $1
              and status = 'pending'
            order by requested_at desc
            """,
            connection))
        {
            command.Parameters.AddWithValue(userId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                fromUserIds.Add(reader.GetInt64(0));
            }
        }

        return new object?[] { await ReadFriendResultListAsync(connection, fromUserIds), fromUserIds.Count };
    }

    public async Task<object?[]> SendFriendRequestAsync(HttpContext context, string? targetUserId)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null || string.IsNullOrWhiteSpace(targetUserId)) return new object?[] { 3 };

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        var targetInternalUserId = await FindUserIdByPublicIdAsync(connection, targetUserId.Trim());
        if (targetInternalUserId is null || targetInternalUserId.Value == userId.Value) return new object?[] { 3 };

        if (await AreUsersBlockedAsync(connection, userId.Value, targetInternalUserId.Value)) return new object?[] { 3 };
        if (await AreUsersFriendsAsync(connection, userId.Value, targetInternalUserId.Value)) return new object?[] { 2 };

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var acceptedReverseRequest = await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                update user_friend_requests
                set status = 'accepted',
                    responded_at = now(),
                    updated_at = now()
                where "fromUserId" = $1
                  and "toUserId" = $2
                  and status = 'pending'
                """,
                targetInternalUserId.Value,
                userId.Value);

            if (acceptedReverseRequest > 0)
            {
                await InsertFriendRowsAsync(connection, transaction, userId.Value, targetInternalUserId.Value);
            }
            else
            {
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    """
                    insert into user_friend_requests ("fromUserId", "toUserId")
                    values ($1, $2)
                    on conflict ("fromUserId", "toUserId") where status = 'pending'
                    do nothing
                    """,
                    userId.Value,
                    targetInternalUserId.Value);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }

        return new object?[] { 1 };
    }

    public async Task<object?[]> CancelFriendRequestAsync(HttpContext context, string? targetUserId)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null || string.IsNullOrWhiteSpace(targetUserId)) return new object?[] { true };

        await using var connection = await database.OpenConnectionAsync();
        var targetInternalUserId = await FindUserIdByPublicIdAsync(connection, targetUserId.Trim());
        if (targetInternalUserId is null) return new object?[] { true };

        await ExecuteNonQueryAsync(
            connection,
            null,
            """
            update user_friend_requests
            set status = 'cancelled',
                responded_at = now(),
                updated_at = now()
            where "fromUserId" = $1
              and "toUserId" = $2
              and status = 'pending'
            """,
            userId.Value,
            targetInternalUserId.Value);
        return new object?[] { true };
    }

    public async Task<object?[]> AcceptFriendRequestAsync(HttpContext context, string? fromUserId)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null || string.IsNullOrWhiteSpace(fromUserId)) return new object?[] { 3 };

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        var fromInternalUserId = await FindUserIdByPublicIdAsync(connection, fromUserId.Trim());
        if (fromInternalUserId is null || fromInternalUserId.Value == userId.Value) return new object?[] { 3 };

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                update user_friend_requests
                set status = 'accepted',
                    responded_at = now(),
                    updated_at = now()
                where "fromUserId" = $1
                  and "toUserId" = $2
                  and status = 'pending'
                """,
                fromInternalUserId.Value,
                userId.Value);

            await InsertFriendRowsAsync(connection, transaction, userId.Value, fromInternalUserId.Value);
            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }

        return new object?[] { 1 };
    }

    public async Task<object?[]> DenyFriendRequestAsync(HttpContext context, string? fromUserId)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null || string.IsNullOrWhiteSpace(fromUserId)) return new object?[] { true };

        await using var connection = await database.OpenConnectionAsync();
        var fromInternalUserId = await FindUserIdByPublicIdAsync(connection, fromUserId.Trim());
        if (fromInternalUserId is null) return new object?[] { true };

        await ExecuteNonQueryAsync(
            connection,
            null,
            """
            update user_friend_requests
            set status = 'denied',
                responded_at = now(),
                updated_at = now()
            where "fromUserId" = $1
              and "toUserId" = $2
              and status = 'pending'
            """,
            fromInternalUserId.Value,
            userId.Value);
        return new object?[] { true };
    }

    public async Task<object?[]> RemoveFriendAsync(HttpContext context, string? targetUserId)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null || string.IsNullOrWhiteSpace(targetUserId)) return new object?[] { true };

        await using var connection = await database.OpenConnectionAsync();
        var targetInternalUserId = await FindUserIdByPublicIdAsync(connection, targetUserId.Trim());
        if (targetInternalUserId is null) return new object?[] { true };

        await DeleteFriendRowsAsync(connection, null, userId.Value, targetInternalUserId.Value);
        return new object?[] { true };
    }

    public async Task<object?[]> BlockFriendUserAsync(HttpContext context, string? targetUserId)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null || string.IsNullOrWhiteSpace(targetUserId)) return new object?[] { true };

        await using var connection = await database.OpenConnectionAsync();
        var targetInternalUserId = await FindUserIdByPublicIdAsync(connection, targetUserId.Trim());
        if (targetInternalUserId is null || targetInternalUserId.Value == userId.Value) return new object?[] { true };

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                insert into user_friend_blocks ("userId", "blockedUserId")
                values ($1, $2)
                on conflict ("userId", "blockedUserId") do nothing
                """,
                userId.Value,
                targetInternalUserId.Value);
            await DeleteFriendRowsAsync(connection, transaction, userId.Value, targetInternalUserId.Value);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                update user_friend_requests
                set status = 'cancelled',
                    responded_at = now(),
                    updated_at = now()
                where status = 'pending'
                  and (("fromUserId" = $1 and "toUserId" = $2)
                    or ("fromUserId" = $2 and "toUserId" = $1))
                """,
                userId.Value,
                targetInternalUserId.Value);
            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }

        return new object?[] { true };
    }

    public async Task<object?[]> RemoveBlockFriendUserAsync(HttpContext context, string? targetUserId)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null || string.IsNullOrWhiteSpace(targetUserId)) return new object?[] { true };

        await using var connection = await database.OpenConnectionAsync();
        var targetInternalUserId = await FindUserIdByPublicIdAsync(connection, targetUserId.Trim());
        if (targetInternalUserId is null) return new object?[] { true };

        await ExecuteNonQueryAsync(
            connection,
            null,
            """
            delete from user_friend_blocks
            where "userId" = $1
              and "blockedUserId" = $2
            """,
            userId.Value,
            targetInternalUserId.Value);
        return new object?[] { true };
    }

    public async Task<object?[]> SetFriendFavoriteAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return new object?[] { true };

        var values = payload as object?[];
        var targetUserId = context.Request.Query["targetUserId"].FirstOrDefault()
            ?? values?.FirstOrDefault(value => value is string)?.ToString();
        var favorite = values?.OfType<bool>().FirstOrDefault() ?? true;
        if (string.IsNullOrWhiteSpace(targetUserId)) return new object?[] { true };

        await using var connection = await database.OpenConnectionAsync();
        var targetInternalUserId = await FindUserIdByPublicIdAsync(connection, targetUserId.Trim());
        if (targetInternalUserId is null) return new object?[] { true };

        await ExecuteNonQueryAsync(
            connection,
            null,
            """
            update user_friend_relationships
            set is_favorite = $3,
                updated_at = now()
            where "userId" = $1
              and "friendUserId" = $2
            """,
            userId.Value,
            targetInternalUserId.Value,
            favorite);
        return new object?[] { true };
    }

    async Task<object?[]?> ReadFriendResultByPublicIdAsync(string publicId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select user_accounts.public_id,
                   user_game_states.rank,
                   user_profiles.comment,
                   user_accounts.updated_at,
                   user_profiles.display_name,
                   user_profiles.home_character_master_id,
                   user_profiles.is_home_character_illust,
                   user_profiles.icon_frame_master_id
            from user_accounts
            join user_game_states on user_game_states."userId" = user_accounts.id
            join user_profiles on user_profiles."userId" = user_accounts.id
            where user_accounts.public_id = $1
            limit 1
            """,
            connection);
        command.Parameters.AddWithValue(publicId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return BuildFriendResult(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetDateTime(3),
            reader.GetString(4),
            GetNullableInt64(reader, 5) ?? DefaultCharacterMasterId,
            reader.GetBoolean(6),
            GetNullableInt64(reader, 7) ?? 0L,
            false);
    }

    static async Task<long?> FindUserIdByPublicIdAsync(NpgsqlConnection connection, string publicId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id
            from user_accounts
            where public_id = $1
            limit 1
            """,
            connection);
        command.Parameters.AddWithValue(publicId);
        var value = await command.ExecuteScalarAsync();
        return value is long userId ? userId : null;
    }

    static async Task<bool> AreUsersFriendsAsync(NpgsqlConnection connection, long userId, long friendUserId)
    {
        await using var command = new NpgsqlCommand(
            """
            select exists (
              select 1
              from user_friend_relationships
              where "userId" = $1
                and "friendUserId" = $2
            )
            """,
            connection);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(friendUserId);
        return await command.ExecuteScalarAsync() is true;
    }

    static async Task<bool> AreUsersBlockedAsync(NpgsqlConnection connection, long userId, long otherUserId)
    {
        await using var command = new NpgsqlCommand(
            """
            select exists (
              select 1
              from user_friend_blocks
              where ("userId" = $1 and "blockedUserId" = $2)
                 or ("userId" = $2 and "blockedUserId" = $1)
            )
            """,
            connection);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(otherUserId);
        return await command.ExecuteScalarAsync() is true;
    }

    static async Task InsertFriendRowsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, long friendUserId)
    {
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            insert into user_friend_relationships ("userId", "friendUserId")
            values ($1, $2), ($2, $1)
            on conflict ("userId", "friendUserId") do nothing
            """,
            userId,
            friendUserId);
    }

    static async Task DeleteFriendRowsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, long userId, long friendUserId)
    {
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            delete from user_friend_relationships
            where ("userId" = $1 and "friendUserId" = $2)
               or ("userId" = $2 and "friendUserId" = $1)
            """,
            userId,
            friendUserId);
    }

    static async Task<object?[][]> ReadFriendResultListAsync(NpgsqlConnection connection, IReadOnlyList<long> userIds)
    {
        var users = new List<object?[]>();
        foreach (var userId in userIds)
        {
            var user = await ReadFriendResultByUserIdAsync(connection, userId, false);
            if (user is not null) users.Add(user);
        }

        return users.ToArray();
    }

    static async Task<object?[]?> ReadFriendResultByUserIdAsync(NpgsqlConnection connection, long userId, bool isFavorite)
    {
        await using var command = new NpgsqlCommand(
            """
            select user_accounts.public_id,
                   user_game_states.rank,
                   user_profiles.comment,
                   user_accounts.updated_at,
                   user_profiles.display_name,
                   user_profiles.home_character_master_id,
                   user_profiles.is_home_character_illust,
                   user_profiles.icon_frame_master_id
            from user_accounts
            join user_game_states on user_game_states."userId" = user_accounts.id
            join user_profiles on user_profiles."userId" = user_accounts.id
            where user_accounts.id = $1
            limit 1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return BuildFriendResult(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetDateTime(3),
            reader.GetString(4),
            GetNullableInt64(reader, 5) ?? DefaultCharacterMasterId,
            reader.GetBoolean(6),
            GetNullableInt64(reader, 7) ?? 0L,
            isFavorite);
    }

    static object?[] BuildFriendResult(
        string publicId,
        int playerRank,
        string introduction,
        DateTime lastLoggedInAt,
        string displayName,
        long mainCharacterMasterId,
        bool displayAwakeningStatus,
        long iconFrameMasterId,
        bool isFavorite)
    {
        return new object?[]
        {
            publicId,
            null,
            playerRank,
            0L,
            0L,
            0L,
            introduction,
            lastLoggedInAt,
            displayName,
            null,
            true,
            0,
            new object?[] { new object?[] { DefaultCharacterBaseMasterId, 1 } },
            true,
            null,
            mainCharacterMasterId,
            displayAwakeningStatus,
            iconFrameMasterId,
            isFavorite
        };
    }
}
