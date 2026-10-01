using Npgsql;

sealed partial class UserDataService
{
    public async Task<object?[]> GetGachaHistoriesAsync(HttpContext context)
    {
        var userId = await RequireAuthenticatedUserAsync(context);

        var cardType = ReadIntQuery(context, "cardType");
        var thingType = cardType switch
        {
            1 => 2,
            2 => 3,
            _ => (int?)null
        };

        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            thingType is null
                ? """
                  select thing_master_id, rolled_at
                  from user_gacha_histories
                  where "userId" = $1
                  order by rolled_at desc, id desc
                  limit 100
                  """
                : """
                  select thing_master_id, rolled_at
                  from user_gacha_histories
                  where "userId" = $1
                    and thing_type = $2
                  order by rolled_at desc, id desc
                  limit 100
                  """,
            connection);
        command.Parameters.AddWithValue(userId);
        if (thingType is not null)
        {
            command.Parameters.AddWithValue(thingType.Value);
        }

        var histories = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            histories.Add(new object?[] { reader.GetInt64(0), reader.GetDateTime(1) });
        }

        return histories.ToArray();
    }

}
