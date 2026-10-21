using System.Text.Json;
using Npgsql;

sealed class GameOperationsService(PostgresDatabase database)
{
    public async Task<GameOperationsSnapshot> GetAsync()
    {
        await using var connection = await database.OpenConnectionAsync();
        return await ReadAsync(connection);
    }

    public static async Task<GameOperationsSnapshot> ReadAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction = null, bool lockForUse = false)
    {
        await using var command = new NpgsqlCommand(
            "select revision,payload::text from system_game_operations where id=1" + (lockForUse ? " for share" : ""),
            connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("活动配置尚未迁移");
        var config = JsonSerializer.Deserialize<GameOperationsConfiguration>(reader.GetString(1), GameOperationsJson.Options)
            ?? throw new InvalidDataException("活动配置为空");
        GameOperationsValidation.Validate(config);
        return new(reader.GetInt64(0), config);
    }

}
