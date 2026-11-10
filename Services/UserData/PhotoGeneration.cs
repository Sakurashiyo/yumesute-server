using System.Security.Cryptography;
using Npgsql;

sealed partial class UserDataService
{
    public sealed record GeneratedPhoto(object?[] Result, object?[] Present);

    public async Task<GeneratedPhoto> GenerateStoredPhotoAsync(HttpContext context, object? body)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        if (body is not object?[] { Length: 5 } values || GetLong(values, 1) is not long itemId
            || values[2] is not byte[] image || values[3] is not byte[] thumbnail
            || values[4] is not object?[] characters || characters.Length is < 1 or > 20)
            throw PhotoFailure(PhotoErrors.InvalidRequest, 400);
        ValidatePhotoJpeg(image, 10 * 1024 * 1024);
        ValidatePhotoJpeg(thumbnail, 1024 * 1024);
        var appeared = new List<object?>();
        var tagged = new List<object?>();
        foreach (var character in characters)
        {
            if (character is not object?[] { Length: 2 } row || GetLong(row, 0) is not long id || !PhotoRules.CharacterIds.Contains(id) || row[1] is not bool tag)
                throw PhotoFailure(PhotoErrors.InvalidRequest, 400);
            appeared.Add(id);
            if (tag) tagged.Add(id);
        }
        if (!PhotoRules.Films.TryGetValue(itemId, out var target) || target is long required && !appeared.Contains(required))
            throw PhotoFailure(PhotoErrors.InvalidFilm, 400);
        // 登录已初始化玩家存档；拍照事务只锁定并更新所需状态，避免重复执行整套存档同步。
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockLessonUserAsync(connection, transaction, userId);
        var hash = Convert.ToHexString(SHA256.HashData(MsgPack.Encode(values)));
        await using (var replay = new NpgsqlCommand("select row_payload from user_photo_media where \"userId\"=$1 and request_hash=$2", connection, transaction))
        {
            replay.Parameters.AddWithValue(userId); replay.Parameters.AddWithValue(hash);
            if (await replay.ExecuteScalarAsync() is byte[] saved)
            {
                var previous = (object?[])MsgPack.Decode(saved)!;
                await transaction.CommitAsync();
                return PhotoResponse(previous);
            }
        }
        await using (var count = new NpgsqlCommand("select count(*) from user_photo_media where \"userId\"=$1", connection, transaction))
        {
            count.Parameters.AddWithValue(userId);
            if (Convert.ToInt64(await count.ExecuteScalarAsync()) >= 120) throw PhotoFailure(PhotoErrors.CapacityReached, 409);
        }
        object?[] possession;
        await using (var consume = new NpgsqlCommand("""
            update user_item_possessions set quantity=quantity-1
            where "userId"=$1 and item_master_id=$2 and quantity>0 returning id,item_master_id,quantity
            """, connection, transaction))
        {
            consume.Parameters.AddWithValue(userId); consume.Parameters.AddWithValue(itemId);
            await using var reader = await consume.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw PhotoFailure(PhotoErrors.FilmInsufficient, 409);
            possession = new object?[] { reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2) };
        }
        long photoId;
        await using (var next = new NpgsqlCommand("select nextval('local_photo_id_seq')", connection, transaction))
            photoId = (long)(await next.ExecuteScalarAsync())!;
        var fileName = $"{Guid.NewGuid():D}.jpg";
        var originalToken = "?sig=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var thumbnailToken = "?sig=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        // 保留现有稀有度规则；本次修复不引入未经确认的官方抽选概率。
        var rowData = new object?[] { photoId, fileName, originalToken, null, false, null, 1, 5, null,
            DateTime.UtcNow.Add(JapanStandardTimeOffset), thumbnailToken, appeared.Distinct().ToArray(), tagged.Distinct().ToArray(), 0 };
        await using (var insert = new NpgsqlCommand("""
            with stored as (
              insert into user_photo_records(id,"userId",file_name,read_sas_token,source_type,source_master_id)
              values($1,$2,$3,$4,1,$5) returning id
            )
            insert into user_photo_media(photo_id,"userId",request_hash,image,thumbnail,row_payload)
            select id,$2,$6,$7,$8,$9 from stored;
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue(photoId); insert.Parameters.AddWithValue(userId);
            insert.Parameters.AddWithValue(fileName); insert.Parameters.AddWithValue(originalToken);
            insert.Parameters.AddWithValue(itemId); insert.Parameters.AddWithValue(hash);
            insert.Parameters.AddWithValue(image); insert.Parameters.AddWithValue(thumbnail);
            insert.Parameters.AddWithValue(MsgPack.Encode(rowData));
            await insert.ExecuteNonQueryAsync();
        }
        await EnsurePhotoAlbumAsync(connection, userId, transaction);
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "照片生成完成 operation=photo-generate userId={UserId} photoId={PhotoId} itemId={ItemId} imageBytes={ImageBytes} outcome=success",
            userId, photoId, itemId, image.Length);
        return PhotoResponse(rowData, possession);
    }

    static GeneratedPhoto PhotoResponse(object?[] row, object?[]? possession = null)
    {
        var present = new List<object?> { DataObject(123, row) };
        if (possession is not null) present.Add(DataObject(27, possession));
        return new GeneratedPhoto(new object?[] { row[0], row[1], row[2], row[7], row[8], row[3] }, present.ToArray());
    }

    static BadHttpRequestException PhotoFailure(string code, int status) => new(code, status);

    static void ValidatePhotoJpeg(byte[] bytes, int maximum)
    {
        if (bytes.Length < 4 || bytes.Length > maximum || bytes[0] != 0xff || bytes[1] != 0xd8
            || bytes[^2] != 0xff || bytes[^1] != 0xd9) throw PhotoFailure(PhotoErrors.InvalidImage, 400);
        var offset = 2;
        var dimensionsFound = false;
        while (offset < bytes.Length - 2)
        {
            if (bytes[offset++] != 0xff) throw PhotoFailure(PhotoErrors.InvalidImage, 400);
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) throw PhotoFailure(PhotoErrors.InvalidImage, 400);
            var marker = bytes[offset++];
            if (marker == 0xda)
            {
                if (dimensionsFound) return;
                throw PhotoFailure(PhotoErrors.InvalidImage, 400);
            }
            if (offset + 2 > bytes.Length) throw PhotoFailure(PhotoErrors.InvalidImage, 400);
            var length = (bytes[offset] << 8) | bytes[offset + 1];
            if (length < 2 || offset + length > bytes.Length) throw PhotoFailure(PhotoErrors.InvalidImage, 400);
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (length < 8) throw PhotoFailure(PhotoErrors.InvalidImage, 400);
                var height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                var width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                if (height == 0 || width == 0 || height > 8192 || width > 8192 || (long)height * width > 32_000_000)
                    throw PhotoFailure(PhotoErrors.InvalidImage, 400);
                dimensionsFound = true;
            }
            offset += length;
        }
        throw PhotoFailure(PhotoErrors.InvalidImage, 400);
    }

    public async Task<byte[]?> ReadPhotoImageAsync(string fileName, bool thumbnail, string token)
    {
        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "D", out _) || !fileName.EndsWith(".jpg", StringComparison.Ordinal)
            || token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            select m.image,m.thumbnail,m.row_payload from user_photo_media m
            join user_photo_records p on p.id=m.photo_id where p.file_name=$1
            """, connection);
        command.Parameters.AddWithValue(fileName);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var row = (object?[])MsgPack.Decode((byte[])reader[2])!;
        var expected = ((string)row[thumbnail ? 10 : 2]!)[5..];
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(token))) return null;
        return (byte[])reader[thumbnail ? 1 : 0];
    }
}
