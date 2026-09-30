using Npgsql;

sealed partial class UserDataService
{
    async Task<long> RequireAuthenticatedUserAsync(HttpContext context)
    {
        var token = ReadToken(context);
        if (string.IsNullOrWhiteSpace(token)) throw new BadHttpRequestException(CircleProtocol.Unauthorized, 401);
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            select "userId" from user_auth_sessions where token_hash = $1 and expires_at > now() order by id desc limit 1
            """, connection);
        command.Parameters.AddWithValue(CryptoService.Sha256(token));
        return await command.ExecuteScalarAsync() is long userId
            ? userId : throw new BadHttpRequestException(CircleProtocol.Unauthorized, 401);
    }
}
