using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> GetNotificationsAsync(HttpContext context)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
              id,
              banner_path,
              title,
              posting_at,
              last_updated_at,
              notification_tab_category,
              notification_category,
              is_confirmation,
              display_order
            from system_notifications
            where starts_at <= now()
              and (ends_at is null or ends_at > now())
            order by display_order asc, posting_at desc, id desc
            """,
            connection);

        var notifications = new List<object?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            notifications.Add(ReadNotificationResult(reader));
        }

        return notifications.ToArray();
    }

    public async Task<object?[]> GetNotificationContentAsync(long id)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
              id,
              body,
              title,
              posting_at,
              last_updated_at,
              notification_tab_category,
              notification_category,
              banner_path
            from system_notifications
            where id = $1
              and starts_at <= now()
              and (ends_at is null or ends_at > now())
            """,
            connection);
        command.Parameters.AddWithValue(id);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new object?[] { id, "", "", DateTime.UtcNow, DateTime.UtcNow, 1, 1, "" };
        }

        return new object?[]
        {
            reader.GetInt64(0),
            NormalizeNotificationBody(reader.GetString(1)),
            reader.GetString(2),
            reader.GetDateTime(3),
            reader.GetDateTime(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetString(7)
        };
    }

    async Task<object?[]> ReadNotificationAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select notification_read_at, notice_read_at, present_read_at
            from user_notification_states
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);
        var unsetAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new object?[] { userId, unsetAt, unsetAt, unsetAt };
        return new object?[]
        {
            userId,
            GetNullableDateTime(reader, 0) ?? unsetAt,
            GetNullableDateTime(reader, 1) ?? unsetAt,
            GetNullableDateTime(reader, 2) ?? unsetAt
        };
    }

    static object?[] ReadNotificationResult(NpgsqlDataReader reader)
    {
        return new object?[]
        {
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetDateTime(3),
            reader.GetDateTime(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetBoolean(7),
            reader.GetInt32(8)
        };
    }
}
