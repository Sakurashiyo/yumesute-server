using Npgsql;

sealed partial class UserDataService
{
    static async Task EnsurePhotoAlbumAsync(NpgsqlConnection connection, long userId, NpgsqlTransaction? transaction = null)
    {
        await using var album = new NpgsqlCommand("""
            with initialized as (
              insert into user_photo_states(id,"userId",photo_category,current_count,max_count)
              values($1,$2,1,1,1) on conflict ("userId",photo_category) do nothing returning id
            )
            insert into user_photo_album_arrangements(id,"userId",album_group_id,arrangement_type,is_public,layout_payload)
            select $3,$2,1,1,false,$4 where not exists(
              select 1 from user_photo_album_arrangements where "userId"=$2 and album_group_id=1)
            on conflict do nothing;
            """, connection, transaction);
        album.Parameters.AddWithValue(UserScopedId(userId, 124001)); album.Parameters.AddWithValue(userId);
        album.Parameters.AddWithValue(UserScopedId(userId, 125010));
        album.Parameters.AddWithValue(MsgPack.Encode(new object?[] { Array.Empty<object?>() }));
        await album.ExecuteNonQueryAsync();
    }

    public async Task<object?[]> GetPhotoAlbumMainPageAsync(HttpContext context, string? targetPublicId)
    {
        var actor = await RequireAuthenticatedUserAsync(context);
        await using var connection = await database.OpenConnectionAsync();
        long owner = actor;
        if (!string.IsNullOrEmpty(targetPublicId))
        {
            await using var target = new NpgsqlCommand("select id from user_accounts where public_id=$1", connection);
            target.Parameters.AddWithValue(targetPublicId);
            if (await target.ExecuteScalarAsync() is not long found) return new object?[] { null, false };
            owner = found;
        }
        await EnsurePhotoAlbumAsync(connection, owner);
        var albums = await ReadPhotoStatesAsync(connection, owner);
        var album = albums.Single(row => Convert.ToInt32(row[1]) == 1);
        var pages = await ReadPhotoAlbumArrangementsAsync(connection, owner);
        var page = pages.Single(row => Convert.ToInt64(row[1]) == Convert.ToInt32(album[2]));
        if (owner != actor && page[3] is not true) return new object?[] { null, false };
        return new object?[] { new object?[] { page[0], album[0], album[1], page[1], page[2], page[3], page[4], page[5] }, true };
    }

    public async Task<object?[]> ArrangePhotoAlbumAsync(HttpContext context, object? payload, int arrangementType)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        if (payload is not object?[] { Length: 4 } values || values[0] is not bool publishing
            || GetLong(values, 1) is not long page || page != 1 || values[2] is not byte[] encoded
            || encoded.Length > 256 * 1024 || values[3] is not null)
            throw PhotoFailure(PhotoErrors.InvalidRequest, 400);
        // 首版只支持已解锁的第一页与默认主题；未知主题不能静默忽略。
        object? decoded;
        try { decoded = MsgPack.Decode(encoded); }
        catch (InvalidDataException error) { throw new BadHttpRequestException(PhotoErrors.InvalidRequest, 400, error); }
        if (decoded is not object?[] { Length: 1 } layout || layout[0] is not object?[] rows || rows.Length > 100)
            throw PhotoFailure(PhotoErrors.InvalidRequest, 400);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        await EnsurePhotoAlbumAsync(connection, userId, transaction);
        var photos = await ReadPhotoRecordsAsync(connection, userId);
        var normalized = new List<object?>();
        foreach (var value in rows)
        {
            if (value is not object?[] row || row.Length != (arrangementType == 1 ? 5 : 12)
                || GetLong(row, 0) is not long photoId)
                throw PhotoFailure(PhotoErrors.InvalidRequest, 400);
            var photo = photos.SingleOrDefault(photo => Convert.ToInt64(photo[0]) == photoId)
                ?? throw PhotoFailure(PhotoErrors.NotFound, 404);
            if (arrangementType == 2 && Enumerable.Range(4, 4).Any(index => row[index] switch
                { double value => !double.IsFinite(value), float value => !float.IsFinite(value),
                  byte or sbyte or short or ushort or int or uint or long => false, _ => true }))
                throw PhotoFailure(PhotoErrors.InvalidRequest, 400);
            var copy = (object?[])row.Clone();
            copy[arrangementType == 1 ? 1 : 2] = photo[1];
            copy[arrangementType == 1 ? 2 : 3] = photo[2];
            normalized.Add(copy);
        }
        var bytes = MsgPack.Encode(new object?[] { normalized.ToArray() });
        var pageId = UserScopedId(userId, 125010);
        await using (var save = new NpgsqlCommand("""
            insert into user_photo_album_arrangements(id,"userId",album_group_id,arrangement_type,is_public,layout_payload)
            values($3,$1,$2,$4,$5,$6)
            on conflict(id) do update set arrangement_type=excluded.arrangement_type,
              is_public=excluded.is_public,layout_payload=excluded.layout_payload,updated_at=now();
            """, connection, transaction))
        {
            save.Parameters.AddWithValue(userId); save.Parameters.AddWithValue(page); save.Parameters.AddWithValue(pageId);
            save.Parameters.AddWithValue(arrangementType); save.Parameters.AddWithValue(publishing); save.Parameters.AddWithValue(bytes);
            await save.ExecuteNonQueryAsync();
        }
        var present = new List<object?> { DataObject(125, new object?[] { pageId, page, arrangementType, publishing, new MsgPack.Binary(bytes), null }) };
        foreach (var photo in photos)
        {
            photo[5] = rows.Cast<object?[]>().Any(row => GetLong(row, 0) == Convert.ToInt64(photo[0])) ? 1 : null;
            await using var update = new NpgsqlCommand("update user_photo_media set row_payload=$2 where photo_id=$1", connection, transaction);
            update.Parameters.AddWithValue(Convert.ToInt64(photo[0])); update.Parameters.AddWithValue(MsgPack.Encode(photo));
            await update.ExecuteNonQueryAsync();
            present.Add(DataObject(123, photo));
        }
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "相册保存完成 operation=photo-album-save userId={UserId} page={Page} photoCount={PhotoCount} outcome=success", userId, page, rows.Length);
        return present.ToArray();
    }
}
