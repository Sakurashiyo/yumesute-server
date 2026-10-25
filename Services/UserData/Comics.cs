using Npgsql;

sealed partial class UserDataService
{
    const int ComicReadRewardType = 13;
    const long ComicReadRewardMasterId = 0;
    const int ComicReadRewardQuantity = 30;

    public async Task<object?[]> ReadComicAsync(HttpContext context, long comicEpisodeId)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // 与其它货币路径共用账号锁，重复点击与并发请求只能领到一次奖励。
            await LockLessonUserAsync(connection, transaction, userId);
            // 已读记录以主键幂等；claimed=false 的历史行不补发，避免旧行为被反向利用。
            await using var claim = new NpgsqlCommand("""
                insert into user_comic_read_states("userId", comic_episode_id, reward_claimed, read_at, updated_at)
                values($1, $2, true, now(), now())
                on conflict ("userId", comic_episode_id) do nothing
                returning reward_claimed
                """, connection, transaction);
            claim.Parameters.AddWithValue(userId);
            claim.Parameters.AddWithValue(comicEpisodeId);
            var firstRead = await claim.ExecuteScalarAsync() is true;
            if (!firstRead)
            {
                await using var seen = new NpgsqlCommand("""
                    update user_comic_read_states set updated_at=now()
                    where "userId"=$1 and comic_episode_id=$2
                    """, connection, transaction);
                seen.Parameters.AddWithValue(userId);
                seen.Parameters.AddWithValue(comicEpisodeId);
                await seen.ExecuteNonQueryAsync();
            }
            else
            {
                await AddFreeJewelsAsync(connection, transaction, userId, ComicReadRewardQuantity);
                await AddCurrencyIncrementAsync(connection, transaction, userId, "free_jewel", ComicReadRewardQuantity);
            }
            await transaction.CommitAsync();
            context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
                "漫画阅读 operation={Operation} userId={UserId} comicEpisodeId={ComicEpisodeId} firstRead={FirstRead}",
                "comic-read", userId, comicEpisodeId, firstRead);
            return firstRead
                ? new object?[] { BuildRewardResult(ComicReadRewardType, ComicReadRewardMasterId, ComicReadRewardQuantity) }
                : Array.Empty<object?>();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }
    }
}
