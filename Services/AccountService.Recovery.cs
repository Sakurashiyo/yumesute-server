using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

sealed partial class AccountService
{
    const int PasswordIterations = 210000;
    const int ConfirmationLifetimeSeconds = 600;
    const int MaximumAttempts = 5;
    // 引继码不存在时用它算一次等长 PBKDF2，抹平"码是否存在"的时间差。
    static readonly byte[] DummySalt = RandomNumberGenerator.GetBytes(16);

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
        // 引继码每个账号只创建一次：客户端设置后即锁定入口，这里也拒绝重复设置。
        // 判定放在消费确认码之前，避免白白浪费一次验证结果。
        if (await ReadRecoveryStateAsync(connection, transaction, userId) is not null)
            throw new BadHttpRequestException(AccountErrors.TakeOverAlreadyUsed, StatusCodes.Status409Conflict);
        // 设置引继密码前必须先通过确认码验证；验证结果只能使用一次。
        await using (var verified = new NpgsqlCommand("""
            delete from user_confirmation_codes where "userId"=$1 and consumed and expires_at>now() returning "userId"
            """, connection, transaction))
        {
            verified.Parameters.AddWithValue(userId);
            if (await verified.ExecuteScalarAsync() is not long)
                throw new BadHttpRequestException(AccountErrors.InvalidConfirmationCode, 401);
        }
        var hash = PasswordHash(password, salt);
        var nonce = CryptoService.RandomToken(24);
        string? code = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await using var insert = new NpgsqlCommand("""
                insert into user_account_recovery("userId",linkage_code,password_salt,password_hash,token_nonce)
                values($1,$2,$3,$4,$5) on conflict do nothing returning linkage_code
                """, connection, transaction);
            var candidate = CryptoService.RandomNumericCode(10);
            insert.Parameters.AddWithValue(userId); insert.Parameters.AddWithValue(candidate);
            insert.Parameters.AddWithValue(salt); insert.Parameters.AddWithValue(hash); insert.Parameters.AddWithValue(nonce);
            code = await insert.ExecuteScalarAsync() as string;
            // 账号行已加锁，唯一冲突只可能来自别的账号占用了同一个 10 位码，换码重试。
            if (code is not null) break;
        }
        if (code is null) throw new InvalidOperationException("无法分配唯一引继码");
        await transaction.CommitAsync();
        return (userId, code);
    }

    public async Task<object?[]> GetTakeOverAccountAsync(HttpContext context, string linkageCode, string password)
    {
        if (linkageCode.Length != 10 || !linkageCode.All(char.IsAsciiDigit) || password.Length is < 8 or > 64)
            throw new BadHttpRequestException(AccountErrors.InvalidTakeOverCredentials, 401);
        // 锁定按 (引继码, 来源 IP)，不再按账号，避免他人知道引继码后反复打错密码锁死号主。
        var attemptKey = RequestGuard.TakeOverKey(context, linkageCode);
        RequestGuard.AssertTakeOverNotLocked(attemptKey);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        long userId;
        byte[] salt, hash; string nonce, publicId, name; bool claimed; int attempts, rank; DateTime? lockedUntil;
        await using (var owner = new NpgsqlCommand("""
            select r."userId",r.password_salt,r.password_hash,r.token_nonce,r.claimed,r.failed_attempts,r.locked_until,
              a.public_id,p.display_name,g.rank from user_account_recovery r join user_accounts a on a.id=r."userId"
              join user_profiles p on p."userId"=a.id join user_game_states g on g."userId"=a.id
            where r.linkage_code=$1
            """, connection, transaction))
        {
            owner.Parameters.AddWithValue(linkageCode);
            await using var reader = await owner.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                // 引继码不存在时也必须付出一次等长 PBKDF2，否则可用响应时间枚举有效引继码。
                _ = PasswordHash(password, DummySalt);
                throw new BadHttpRequestException(AccountErrors.InvalidTakeOverCredentials, 401);
            }
            userId = reader.GetInt64(0);
            salt = reader.GetFieldValue<byte[]>(1); hash = reader.GetFieldValue<byte[]>(2); nonce = reader.GetString(3);
            claimed = reader.GetBoolean(4); attempts = reader.GetInt32(5); lockedUntil = reader.IsDBNull(6) ? null : reader.GetDateTime(6);
            publicId = reader.GetString(7); name = reader.GetString(8); rank = reader.GetInt32(9);
        }
        await LockAccountAsync(connection, transaction, userId);
        if (lockedUntil > DateTime.UtcNow) throw new BadHttpRequestException(AccountErrors.RecoveryRateLimited, 429);
        if (lockedUntil is not null) attempts = 0;
        // 引继码一次性：已领取的码直接失败，且不消耗失败次数（码本身再也无法通过验证）。
        // 密码比对照常执行，避免「已用过的码」与「密码错误」出现时间差。
        var passwordMatches = CryptographicOperations.FixedTimeEquals(hash, PasswordHash(password, salt));
        if (claimed) throw new BadHttpRequestException(AccountErrors.InvalidTakeOverCredentials, 401);
        if (!passwordMatches)
        {
            RequestGuard.RecordTakeOverFailure(attemptKey);
            var failures = attempts + 1;
            await using var fail = new NpgsqlCommand("update user_account_recovery set failed_attempts=$2,locked_until=$3 where \"userId\"=$1", connection, transaction);
            fail.Parameters.AddWithValue(userId); fail.Parameters.AddWithValue(failures);
            fail.Parameters.Add(new NpgsqlParameter { Value = failures >= MaximumAttempts ? DateTime.UtcNow.AddMinutes(5) : DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz });
            await fail.ExecuteNonQueryAsync(); await transaction.CommitAsync();
            throw new BadHttpRequestException(AccountErrors.InvalidTakeOverCredentials, 401);
        }
        RequestGuard.ClearTakeOverFailures(attemptKey);
        var loginToken = CryptoService.RandomToken(32);
        foreach (var sql in new[] { "delete from user_auth_sessions where \"userId\"=$1", "delete from user_login_identities where \"userId\"=$1" })
        {
            await using var revoke = new NpgsqlCommand(sql, connection, transaction);
            revoke.Parameters.AddWithValue(userId);
            await revoke.ExecuteNonQueryAsync();
        }
        await InsertLocalLoginIdentityAsync(connection, transaction, CryptoService.Sha256(loginToken), userId, "GooglePlay", "", "", config.ApplicationVersion);
        // 就地作废引继凭据：引继码与密码都已经无法再次使用，只有 newly issued login token 有效。
        await using var complete = new NpgsqlCommand("""
            update user_account_recovery set claimed=true,token_nonce=$2,
              password_salt=$3,password_hash=$4,
              failed_attempts=0,locked_until=null,updated_at=now()
            where "userId"=$1
            """, connection, transaction);
        var revokedSalt = RandomNumberGenerator.GetBytes(16);
        complete.Parameters.AddWithValue(userId);
        complete.Parameters.AddWithValue(CryptoService.RandomToken(24));
        complete.Parameters.AddWithValue(revokedSalt);
        complete.Parameters.AddWithValue(PasswordHash(CryptoService.RandomToken(32), revokedSalt));
        await complete.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return new object?[] { true, publicId, name, rank, loginToken };
    }

    // 只用于判断该账号是否已有引继码；引继码本身不对外暴露，也不允许二次创建。
    sealed record RecoveryState(string LinkageCode);

    static async Task<RecoveryState?> ReadRecoveryStateAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long userId)
    {
        await using var command = new NpgsqlCommand("""
            select linkage_code from user_account_recovery where "userId"=$1 for update
            """, connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new RecoveryState(reader.GetString(0)) : null;
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
