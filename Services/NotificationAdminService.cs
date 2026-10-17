using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

sealed record NotificationAdminRow(long Id, long Revision, string Title, string Body, string BannerPath,
    DateTimeOffset PostingAt, DateTimeOffset LastUpdatedAt, DateTimeOffset StartsAt, DateTimeOffset? EndsAt,
    int NotificationTabCategory, int NotificationCategory, bool IsConfirmation, int DisplayOrder);

static class NotificationAdminErrors
{
    public const string Invalid = "NOTIFICATION_INVALID";
    public const string Conflict = "NOTIFICATION_REVISION_CONFLICT";
    public const string BannerUnavailable = "NOTIFICATION_BANNER_UNAVAILABLE";
}

sealed class NotificationAdminService(PostgresDatabase database, NotificationBannerCatalog banners)
{
    const string Columns = """
        id,revision,title,body,banner_path,posting_at,last_updated_at,starts_at,ends_at,
        notification_tab_category,notification_category,is_confirmation,display_order
        """;
    public async Task<NotificationAdminRow[]> ListAsync()
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"select {Columns} from system_notifications order by display_order,posting_at desc,id desc", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<NotificationAdminRow>();
        while (await reader.ReadAsync())
            rows.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetDateTime(5), reader.GetDateTime(6), reader.GetDateTime(7), reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                reader.GetInt32(9), reader.GetInt32(10), reader.GetBoolean(11), reader.GetInt32(12)));
        return rows.ToArray();
    }
    public async Task<long> SaveAsync(NotificationAdminRow row)
    {
        Validate(row);
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(row.Id == 0 ? """
            insert into system_notifications(title,body,banner_path,posting_at,last_updated_at,starts_at,ends_at,
                notification_tab_category,notification_category,is_confirmation,display_order)
            values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11) returning id
            """ : """
            update system_notifications set title=$1,body=$2,banner_path=$3,posting_at=$4,last_updated_at=$5,
                starts_at=$6,ends_at=$7,notification_tab_category=$8,notification_category=$9,
                is_confirmation=$10,display_order=$11,revision=revision+1,updated_at=now()
            where id=$12 and revision=$13 returning id
            """, connection);
        command.Parameters.AddWithValue(row.Title.Trim());
        command.Parameters.AddWithValue(row.Body);
        command.Parameters.AddWithValue(row.BannerPath);
        command.Parameters.AddWithValue(row.PostingAt.UtcDateTime);
        command.Parameters.AddWithValue(row.LastUpdatedAt.UtcDateTime);
        command.Parameters.AddWithValue(row.StartsAt.UtcDateTime);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)row.EndsAt?.UtcDateTime ?? DBNull.Value });
        command.Parameters.AddWithValue(row.NotificationTabCategory);
        command.Parameters.AddWithValue(row.NotificationCategory);
        command.Parameters.AddWithValue(row.IsConfirmation);
        command.Parameters.AddWithValue(row.DisplayOrder);
        if (row.Id != 0) { command.Parameters.AddWithValue(row.Id); command.Parameters.AddWithValue(row.Revision); }
        var id = await command.ExecuteScalarAsync();
        if (id is null) throw new GameOperationsException(NotificationAdminErrors.Conflict, "公告已被修改或不存在，请刷新后重试", 409);
        return Convert.ToInt64(id);
    }
    void Validate(NotificationAdminRow row)
    {
        if (row.Id < 0 || row.Revision < 0 || string.IsNullOrWhiteSpace(row.Title) || row.Title.Length > 200
            || row.NotificationTabCategory is < 1 or > 3 || row.NotificationCategory is < 1 or > 6
            || row.DisplayOrder is < -100000 or > 100000 || row.EndsAt <= row.StartsAt
            || new[] { row.StartsAt, row.PostingAt, row.LastUpdatedAt }.Any(date => date.Year is < 2020 or > 2098)
            || row.EndsAt?.Year > 2099)
            throw new GameOperationsException(NotificationAdminErrors.Invalid, "公告标题、分类、顺序或开放时间无效");
        if (row.BannerPath is null || !banners.Available().Any(banner => banner.Path == row.BannerPath))
            throw new GameOperationsException(NotificationAdminErrors.BannerUnavailable, "请选择服务端实际存在的游戏 banner");
        if (row.Body is null || row.Body.Length is < 2 or > 48000)
            throw new GameOperationsException(NotificationAdminErrors.Invalid, "公告正文长度无效");
        using var document = ParseBody(row.Body);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() is < 1 or > 100)
            throw new GameOperationsException(NotificationAdminErrors.Invalid, "正文需要 1–100 个内容块");
        foreach (var block in document.RootElement.EnumerateArray())
            if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("Element", out var element)
                || element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var type) || type is < 2 or > 6
                || !block.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.String
                || data.GetString()!.Length > 12000 || block.EnumerateObject().Count() != 2)
                throw new GameOperationsException(NotificationAdminErrors.Invalid, "正文块格式无效");
    }
    static JsonDocument ParseBody(string body)
    {
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { throw new GameOperationsException(NotificationAdminErrors.Invalid, "公告正文不是有效的内容块 JSON"); }
    }
}
