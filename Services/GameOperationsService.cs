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

    public async Task<GameOperationsSnapshot> SaveAsync(GameOperationsSave request)
    {
        if (request.Revision < 0) throw new GameOperationsException(GameOperationsErrors.Invalid, "配置版本无效");
        GameOperationsValidation.Validate(request.Configuration);
        await using var connection = await database.OpenConnectionAsync();
        // 比较并交换同一行；进行中的业务持有共享锁，配置切换只在完整发奖之后生效。
        await using var command = new NpgsqlCommand("""
            update system_game_operations set payload=$2::jsonb,revision=revision+1,updated_at=now()
            where id=1 and revision=$1 returning revision
            """, connection);
        command.Parameters.AddWithValue(request.Revision);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(request.Configuration, GameOperationsJson.Options));
        var revision = await command.ExecuteScalarAsync();
        if (revision is null)
            throw new GameOperationsException(GameOperationsErrors.Conflict, "配置已被其他页面修改，请刷新后重试", 409);
        return new(Convert.ToInt64(revision), request.Configuration);
    }
}
