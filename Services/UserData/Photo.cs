using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> ArrangePhotoAlbumAsync(HttpContext context, object? payload, int arrangementType)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        var values = payload as object?[] ?? Array.Empty<object?>();
        var isPublic = GetBool(values, 0) ?? true;
        var albumGroupId = GetLong(values, 1) ?? 1;
        var rawLayout = values.ElementAtOrDefault(2);
        var layout = DecodePhotoAlbumLayout(rawLayout);
        var normalizedLayout = NormalizePhotoAlbumLayout(layout, arrangementType);
        var encodedLayout = MsgPack.Encode(normalizedLayout);
        var arrangementId = userId is long currentUserId
            ? UserScopedId(currentUserId, 125000 + albumGroupId * 10 + arrangementType)
            : Random.Shared.Next(300000, 399999);

        if (userId is not null)
        {
            await EnsureDefaultUserDataAsync(userId.Value);
            await using var connection = await database.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                """
                insert into user_photo_album_arrangements (
                  id, "userId", album_group_id, arrangement_type, is_public, layout_payload, updated_at
                )
                values ($1, $2, $3, $4, $5, $6, now())
                on conflict ("userId", album_group_id, arrangement_type)
                do update set is_public = excluded.is_public,
                              layout_payload = excluded.layout_payload,
                              updated_at = now()
                returning id
                """,
                connection);
            command.Parameters.AddWithValue(arrangementId);
            command.Parameters.AddWithValue(userId.Value);
            command.Parameters.AddWithValue(albumGroupId);
            command.Parameters.AddWithValue(arrangementType);
            command.Parameters.AddWithValue(isPublic);
            command.Parameters.AddWithValue(encodedLayout);
            var returnedId = await command.ExecuteScalarAsync();
            if (returnedId is long id) arrangementId = id;
        }

        var presentData = new List<object?>
        {
            DataObject(125, new object?[]
            {
                arrangementId,
                albumGroupId,
                arrangementType,
                isPublic,
                new MsgPack.Binary(encodedLayout),
                null
            }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, arrangementType, null, 11, 11 })
        };

        if (arrangementType == 1)
        {
            var firstPhoto = FirstPhotoId(normalizedLayout) ?? Random.Shared.Next(39000000, 39999999);
            var fileName = BuildPhotoFileName(firstPhoto);
            var sasToken = GameResults.BuildPhotoSasToken("photo");
            var thumbnailSasToken = GameResults.BuildPhotoSasToken("thumbnail");

            presentData.Add(DataObject(123, new object?[]
            {
                firstPhoto,
                fileName,
                sasToken,
                null,
                false,
                albumGroupId,
                1,
                5,
                null,
                DateTime.UtcNow,
                thumbnailSasToken,
                new object?[] { 404, 104 },
                new object?[] { 404, 104 },
                0
            }));
            presentData.Add(DataObject(124, new object?[] { UserScopedId(userId ?? 1, 124000 + albumGroupId), 1, 1, 1 }));
            presentData.Add(DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 4, null, 12, 12 }));
            presentData.Add(DataObject(48, new object?[] { 126984517L, false, false, 14, null, 42, 42 }));
        }

        return presentData.ToArray();
    }

    static object?[] DecodePhotoAlbumLayout(object? rawLayout)
    {
        if (rawLayout is byte[] bytes && bytes.Length > 0)
        {
            return MsgPack.Decode(bytes) as object?[] ?? Array.Empty<object?>();
        }

        return rawLayout as object?[] ?? Array.Empty<object?>();
    }

    static object?[] NormalizePhotoAlbumLayout(object?[] layout, int arrangementType)
    {
        var rows = layout.Length == 1 && layout[0] is object?[] nestedRows
            ? nestedRows
            : layout;

        var normalizedRows = rows
            .OfType<object?[]>()
            .Select(row => arrangementType == 2
                ? NormalizeDetailPhotoAlbumRow(row)
                : NormalizeSimplePhotoAlbumRow(row))
            .ToArray();

        return new object?[] { normalizedRows };
    }

    static object?[] NormalizeSimplePhotoAlbumRow(object?[] row)
    {
        var photoId = GetLong(row, 0) ?? 0;
        return new object?[]
        {
            photoId,
            BuildPhotoFileName(photoId),
            GameResults.BuildPhotoSasToken("photo"),
            GetLong(row, 3) ?? 0,
            row.ElementAtOrDefault(4)
        };
    }

    static object?[] NormalizeDetailPhotoAlbumRow(object?[] row)
    {
        var photoId = GetLong(row, 0) ?? 0;
        return new object?[]
        {
            photoId,
            GetLong(row, 1) ?? 1,
            BuildPhotoFileName(photoId),
            GameResults.BuildPhotoSasToken("photo"),
            ReadDouble(row, 4),
            ReadDouble(row, 5),
            ReadDouble(row, 6),
            ReadDouble(row, 7, 1.0),
            GetLong(row, 8) ?? 0,
            GetBool(row, 9) ?? true,
            GetBool(row, 10) ?? false,
            row.ElementAtOrDefault(11)
        };
    }

    static long? FirstPhotoId(object?[] normalizedLayout)
    {
        var rows = normalizedLayout.ElementAtOrDefault(0) as object?[];
        var firstRow = rows?.OfType<object?[]>().FirstOrDefault();
        return firstRow is null ? null : GetLong(firstRow, 0);
    }

    static double ReadDouble(object?[] values, int index, double defaultValue = 0.0)
    {
        if (index >= values.Length) return defaultValue;
        return values[index] switch
        {
            double value => value,
            float value => value,
            int value => value,
            long value => value,
            string text when double.TryParse(text, out var value) => value,
            _ => defaultValue
        };
    }

    static string BuildPhotoFileName(long photoId)
    {
        return photoId > 0
            ? $"{photoId:x8}-local-photo.jpg"
            : $"{Guid.NewGuid():D}.jpg";
    }
}
