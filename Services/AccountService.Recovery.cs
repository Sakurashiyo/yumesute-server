using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

sealed partial class AccountService
{
    const int PasswordIterations = 210000;
    const int ConfirmationLifetimeSeconds = 600;
    const int MaximumAttempts = 5;

    public async Task<object?[]> GetConfirmationCodeAsync(string? apiToken)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var userId = await LockSessionAccountAsync(connection, transaction, apiToken);
        var now = DateTime.UtcNow;
        await using var command = new NpgsqlCommand("""
            insert into user_confirmation_codes("userId",nonce,expires_at) values($1,$2,$3)
            on conflict ("userId") do update set nonce=excluded.nonce,expires_at=excluded.expires_at,
              consumed=false,failed_attempts=0
              where user_confirmation_codes.expires_at<=$4 or user_confirmation_codes.consumed
            """, connection, transaction);
        command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(CryptoService.RandomToken(24));
        command.Parameters.AddWithValue(now.AddSeconds(ConfirmationLifetimeSeconds)); command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync();
        await using var read = new NpgsqlCommand("select nonce,expires_at,failed_attempts from user_confirmation_codes where \"userId\"=$1", connection, transaction);
        read.Parameters.AddWithValue(userId);
        string nonce; DateTime expires; int attempts;
        await using (var reader = await read.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new InvalidOperationException("确认码写入后缺少对应记录");
            nonce = reader.GetString(0); expires = reader.GetDateTime(1); attempts = reader.GetInt32(2);
        }
        if (attempts >= MaximumAttempts) throw new BadHttpRequestException(AccountErrors.RecoveryRateLimited, 429);
        var seconds = Math.Clamp((int)Math.Ceiling((expires - DateTime.UtcNow).TotalSeconds), 0, ConfirmationLifetimeSeconds);
        await transaction.CommitAsync();
        return new object?[] { ConfirmationCode(userId, nonce), seconds };
    }

    public async Task VerifyConfirmationCodeAsync(string? apiToken, string code)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var userId = await LockSessionAccountAsync(connection, transaction, apiToken);
        string? nonce = null; DateTime expires = default; bool consumed = false; int attempts = 0;
        await using (var read = new NpgsqlCommand("select nonce,expires_at,consumed,failed_attempts from user_confirmation_codes where \"userId\"=$1", connection, transaction))
        {
            read.Parameters.AddWithValue(userId);
            await using var reader = await read.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            { nonce = reader.GetString(0); expires = reader.GetDateTime(1); consumed = reader.GetBoolean(2); attempts = reader.GetInt32(3); }
        }
        if (nonce is null || consumed || expires <= DateTime.UtcNow)
            throw new BadHttpRequestException(AccountErrors.InvalidConfirmationCode, 401);
        if (attempts >= MaximumAttempts) throw new BadHttpRequestException(AccountErrors.RecoveryRateLimited, 429);
        var valid = code.Length == 6 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(ConfirmationCode(userId, nonce)));
        await using var update = new NpgsqlCommand("update user_confirmation_codes set consumed=$2,failed_attempts=failed_attempts+$3 where \"userId\"=$1", connection, transaction);
        update.Parameters.AddWithValue(userId); update.Parameters.AddWithValue(valid); update.Parameters.AddWithValue(valid ? 0 : 1);
        await update.ExecuteNonQueryAsync();
        // 验证失败次数必须提交后再返回失败，否则攻击者可通过回滚绕过限制。
        await transaction.CommitAsync();
        if (!valid) throw new BadHttpRequestException(AccountErrors.InvalidConfirmationCode, 401);
    }

    public async Task<(long UserId, string LinkageCode)> RegisterTakeOverPasswordAsync(string? apiToken, string password)
    {
        if (password.Length is < 8 or > 64 || string.IsNullOrWhiteSpace(password))
            throw new BadHttpRequestException(AccountErrors.InvalidTakeOverPassword, 400);
        var salt = RandomNumberGenerator.GetBytes(16);

        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var userId = await LockSessionAccountAsync(connection, transaction, apiToken);
        var hash = PasswordHash(password, salt);
        string? code = null;
        var nonce = CryptoService.RandomToken(24);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = CryptoService.RandomNumericCode(10);
            await using var insert = new NpgsqlCommand("""
                insert into user_account_recovery("userId",linkage_code,password_salt,password_hash,token_nonce)
                values($1,$2,$3,$4,$5) on conflict do nothing returning linkage_code
                """, connection, transaction);
            insert.Parameters.AddWithValue(userId); insert.Parameters.AddWithValue(candidate);
            insert.Parameters.AddWithValue(salt); insert.Parameters.AddWithValue(hash); insert.Parameters.AddWithValue(nonce);
            code = await insert.ExecuteScalarAsync() as string;
            if (code is not null) break;
            // 已有引继码保持稳定；改密更新哈希和签发版本，旧密码立即失效。
            await using var update = new NpgsqlCommand("""
                update user_account_recovery set password_salt=$2,password_hash=$3,token_nonce=$4,
                  claimed=false,failed_attempts=0,locked_until=null,updated_at=now()
                where "userId"=$1 returning linkage_code
                """, connection, transaction);
            update.Parameters.AddWithValue(userId); update.Parameters.AddWithValue(salt); update.Parameters.AddWithValue(hash); update.Parameters.AddWithValue(nonce);
            code = await update.ExecuteScalarAsync() as string;
            if (code is not null) break;
        }
        if (code is null) throw new InvalidOperationException("无法分配唯一引继码");
        await transaction.CommitAsync();
        return (userId, code);
    }

    public async Task<object?[]> GetTakeOverAccountAsync(string linkageCode, string password)
    {
        if (linkageCode.Length != 10 || !linkageCode.All(char.IsAsciiDigit) || password.Length is < 8 or > 64)
            throw new BadHttpRequestException(AccountErrors.InvalidTakeOverCredentials, 401);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        long userId;
        await using (var owner = new NpgsqlCommand("select \"userId\" from user_account_recovery where linkage_code=$1", connection, transaction))
        {
            owner.Parameters.AddWithValue(linkageCode);
            if (await owner.ExecuteScalarAsync() is not long found) throw new BadHttpRequestException(AccountErrors.InvalidTakeOverCredentials, 401);
            userId = found;
        }
        await LockAccountAsync(connection, transaction, userId);
        byte[] salt, hash; string nonce, publicId, name; bool claimed; int attempts, rank; DateTime? lockedUntil;
        await using (var read = new NpgsqlCommand("""
            select r.password_salt,r.password_hash,r.token_nonce,r.claimed,r.failed_attempts,r.locked_until,
              a.public_id,p.display_name,g.rank from user_account_recovery r join user_accounts a on a.id=r."userId"
              join user_profiles p on p."userId"=a.id join user_game_states g on g."userId"=a.id
              where r."userId"=$1 and r.linkage_code=$2
            """, connection, transaction))
        {
            read.Parameters.AddWithValue(userId); read.Parameters.AddWithValue(linkageCode);
            await using var reader = await read.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new BadHttpRequestException(AccountErrors.InvalidTakeOverCredentials, 401);
            salt = reader.GetFieldValue<byte[]>(0); hash = reader.GetFieldValue<byte[]>(1); nonce = reader.GetString(2);
            claimed = reader.GetBoolean(3); attempts = reader.GetInt32(4); lockedUntil = reader.IsDBNull(5) ? null : reader.GetDateTime(5);
            publicId = reader.GetString(6); name = reader.GetString(7); rank = reader.GetInt32(8);
        }
        if (lockedUntil > DateTime.UtcNow) throw new BadHttpRequestException(AccountErrors.RecoveryRateLimited, 429);
        if (lockedUntil is not null) attempts = 0;
        if (!CryptographicOperations.FixedTimeEquals(hash, PasswordHash(password, salt)))
        {
            var failures = attempts + 1;
            await using var fail = new NpgsqlCommand("update user_account_recovery set failed_attempts=$2,locked_until=$3 where \"userId\"=$1", connection, transaction);
            fail.Parameters.AddWithValue(userId); fail.Parameters.AddWithValue(failures);
            fail.Parameters.Add(new NpgsqlParameter { Value = failures >= MaximumAttempts ? DateTime.UtcNow.AddMinutes(5) : DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz });
            await fail.ExecuteNonQueryAsync(); await transaction.CommitAsync();
            throw new BadHttpRequestException(AccountErrors.InvalidTakeOverCredentials, 401);
        }
        var loginToken = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(config.TokenSecret), Encoding.UTF8.GetBytes($"account-takeover:{userId}:{nonce}")))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (!claimed)
        {
            foreach (var sql in new[] { "delete from user_auth_sessions where \"userId\"=$1", "delete from user_login_identities where \"userId\"=$1" })
            {
                await using var revoke = new NpgsqlCommand(sql, connection, transaction);
                revoke.Parameters.AddWithValue(userId);
                await revoke.ExecuteNonQueryAsync();
            }
            await InsertLocalLoginIdentityAsync(connection, transaction, CryptoService.Sha256(loginToken), userId, "GooglePlay", "", "", config.ApplicationVersion);
        }
        await using var complete = new NpgsqlCommand("update user_account_recovery set claimed=true,failed_attempts=0,locked_until=null where \"userId\"=$1", connection, transaction);
        complete.Parameters.AddWithValue(userId); await complete.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return new object?[] { true, publicId, name, rank, loginToken };
    }

    string ConfirmationCode(long userId, string nonce)
    {
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(config.TokenSecret), Encoding.UTF8.GetBytes($"account-confirmation:{userId}:{nonce}"));
        return (BinaryPrimitives.ReadUInt32BigEndian(digest) % 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    static byte[] PasswordHash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA512, 32);

    static async Task LockAccountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        await using var command = new NpgsqlCommand("select id from user_accounts where id=$1 for update", connection, transaction);
        command.Parameters.AddWithValue(userId);
        if (await command.ExecuteScalarAsync() is not long) throw new BadHttpRequestException(AccountErrors.Unauthorized, 401);
    }

    static async Task<long> LockSessionAccountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string? apiToken)
    {
        if (string.IsNullOrWhiteSpace(apiToken)) throw new BadHttpRequestException(AccountErrors.Unauthorized, 401);
        var hash = CryptoService.Sha256(apiToken);
        async Task<long?> ReadSession()
        {
            await using var query = new NpgsqlCommand("select \"userId\" from user_auth_sessions where token_hash=$1 and expires_at>now() order by id desc limit 1", connection, transaction);
            query.Parameters.AddWithValue(hash); return await query.ExecuteScalarAsync() is long id ? id : null;
        }
        var userId = await ReadSession() ?? throw new BadHttpRequestException(AccountErrors.Unauthorized, 401);
        await LockAccountAsync(connection, transaction, userId);
        if (await ReadSession() != userId) throw new BadHttpRequestException(AccountErrors.Unauthorized, 401);
        return userId;
    }
}
