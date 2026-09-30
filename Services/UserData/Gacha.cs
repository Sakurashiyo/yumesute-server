using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    public async Task SaveGachaHistoryAsync(HttpContext context, long gachaDetailMasterId, object?[] gachaRollResult)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null || gachaRollResult.Length < 2 || gachaRollResult[1] is not object?[] rewards) return;

        var histories = ExtractGachaHistoryRows(gachaDetailMasterId, rewards);
        if (histories.Count == 0) return;

        await EnsureDefaultUserDataAsync(userId.Value);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            foreach (var history in histories)
            {
                await using var command = new NpgsqlCommand(
                    """
                    insert into user_gacha_histories (
                      "userId",
                      gacha_detail_master_id,
                      thing_type,
                      thing_master_id,
                      quantity,
                      payload
                    )
                    values ($1, $2, $3, $4, $5, $6::jsonb)
                    """,
                    connection,
                    transaction);
                command.Parameters.AddWithValue(userId.Value);
                command.Parameters.AddWithValue(history.GachaDetailMasterId);
                command.Parameters.AddWithValue(history.ThingType);
                command.Parameters.AddWithValue(history.ThingMasterId);
                command.Parameters.AddWithValue(history.Quantity);
                command.Parameters.AddWithValue(JsonSerializer.Serialize(history.Payload));
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<object?[]> GetGachaHistoriesAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null) return Array.Empty<object?>();

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
        command.Parameters.AddWithValue(userId.Value);
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

    static List<GachaHistoryRow> ExtractGachaHistoryRows(long gachaDetailMasterId, object?[] rewards)
    {
        var rows = new List<GachaHistoryRow>();
        foreach (var reward in rewards)
        {
            if (reward is not object?[] rewardValues || rewardValues.Length == 0) continue;
            if (rewardValues[0] is not object?[] receivedThings) continue;

            foreach (var receivedThingWrapper in receivedThings)
            {
                var receivedThing = receivedThingWrapper as object?[];
                if (receivedThing is null || receivedThing.Length == 1)
                {
                    receivedThing = receivedThing?.FirstOrDefault() as object?[];
                }

                if (receivedThing is null || receivedThing.Length < 3) continue;
                var thingType = GetInt(receivedThing, 0);
                var thingMasterId = GetLong(receivedThing, 1);
                var quantity = GetInt(receivedThing, 2) ?? 1;
                if (thingType is null || thingMasterId is null) continue;
                if (thingType.Value is not (2 or 3)) continue;

                rows.Add(new GachaHistoryRow(
                    gachaDetailMasterId,
                    thingType.Value,
                    thingMasterId.Value,
                    quantity,
                    receivedThing));
            }
        }

        return rows;
    }
}
