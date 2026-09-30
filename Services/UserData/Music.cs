using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> EditMusicBookmarkAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null) return Array.Empty<object?>();

        var values = payload as object?[] ?? Array.Empty<object?>();
        var musicMasterId = GetLong(values, 0) ?? 0;
        var bookmarkState = (int)(GetLong(values, 1) ?? 0);

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into user_music_bookmarks (id, "userId", music_master_id, bookmark_state, updated_at)
            values ($1, $2, $3, $4, now())
            on conflict ("userId", music_master_id)
            do update set bookmark_state = excluded.bookmark_state,
                          updated_at = now()
            """,
            connection);
        command.Parameters.AddWithValue(UserScopedId(userId.Value, 1460000 + musicMasterId));
        command.Parameters.AddWithValue(userId.Value);
        command.Parameters.AddWithValue(musicMasterId);
        command.Parameters.AddWithValue(bookmarkState);
        await command.ExecuteNonQueryAsync();

        // key 146：音乐收藏夹状态。第二个值不是 bool，而是收藏夹编号/标志位；
        // 0 表示从收藏夹移除，1/2/4 等值需要按客户端原样保存和下发。
        return new object?[]
        {
            DataObject(146, new object?[] { musicMasterId, bookmarkState })
        };
    }
}
