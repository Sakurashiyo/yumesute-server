using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> UpdateShopLastViewedAtAsync(HttpContext context, object? payload)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null) return Array.Empty<object?>();

        var values = payload as object?[] ?? Array.Empty<object?>();
        var shopCategory = (int)(GetLong(values, 0) ?? 0);
        var shopMasterId = GetNullableLong(values, 1);
        var viewKey = $"{shopCategory}:{shopMasterId?.ToString() ?? ""}";

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into user_shop_last_views ("userId", shop_category, shop_master_id, view_key, last_viewed_at, updated_at)
            values ($1, $2, $3, $4, now(), now())
            on conflict ("userId", view_key)
            do update set shop_category = excluded.shop_category,
                          shop_master_id = excluded.shop_master_id,
                          last_viewed_at = now(),
                          updated_at = now()
            returning id, shop_master_id, last_viewed_at, shop_category
            """,
            connection);
        command.Parameters.AddWithValue(userId.Value);
        command.Parameters.AddWithValue(shopCategory);
        command.Parameters.AddWithValue((object?)shopMasterId ?? DBNull.Value);
        command.Parameters.AddWithValue(viewKey);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new object?[]
        {
            DataObject(65, new object?[]
            {
                reader.GetInt64(0),
                GetNullableInt64(reader, 1),
                reader.GetDateTime(2),
                reader.GetInt32(3)
            })
        };
    }
}
