using Npgsql;

sealed partial class UserDataService
{
    static readonly TimeSpan JapanStandardTimeOffset = TimeSpan.FromHours(9);

    async Task<object?[]> ReadDailyLimitAsync(NpgsqlConnection connection, long userId, NpgsqlTransaction? transaction = null, DateTimeOffset? utcNow = null)
    {
        var resetBoundary = CurrentDailyResetBoundaryUtc(utcNow ?? DateTimeOffset.UtcNow);
        await using (var reset = new NpgsqlCommand(
            """
            insert into user_daily_limits (id, "userId", auto_play_times, daily_lesson_times, last_refreshed_at, music_course_free_challenge_times)
            values ($1, $2, 0, 0, $3, 0)
            on conflict ("userId") do update set
              daily_lesson_times = 0,
              last_refreshed_at = excluded.last_refreshed_at,
              updated_at = now()
            where user_daily_limits.last_refreshed_at < $3
            """, connection, transaction))
        {
            reset.Parameters.AddWithValue(UserScopedId(userId, 10902));
            reset.Parameters.AddWithValue(userId);
            reset.Parameters.AddWithValue(resetBoundary.UtcDateTime);
            await reset.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            """
            select id, auto_play_times, daily_lesson_times, last_refreshed_at, music_course_free_challenge_times
            from user_daily_limits
            where "userId" = $1
            """, connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException($"用户 {userId} 缺少每日限制状态");
        return new object?[]
        {
            reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetDateTime(3), reader.GetInt32(4)
        };
    }

    static DateTimeOffset CurrentDailyResetBoundaryUtc(DateTimeOffset utcNow)
    {
        var japanNow = utcNow.ToOffset(JapanStandardTimeOffset);
        var resetDate = japanNow.TimeOfDay < TimeSpan.FromHours(5)
            ? japanNow.Date.AddDays(-1)
            : japanNow.Date;
        return new DateTimeOffset(resetDate.AddHours(5), JapanStandardTimeOffset).ToUniversalTime();
    }
}
