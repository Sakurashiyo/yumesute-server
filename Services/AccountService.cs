using Npgsql;

sealed partial class AccountService
{
    readonly LocalConfig config;
    readonly PostgresDatabase database;

    public AccountService(LocalConfig config, PostgresDatabase database)
    {
        this.config = config;
        this.database = database;
    }

    public async Task<RegisterGameAccountResult> RegisterGameAccountAsync(string name)
    {
        var loginToken = CryptoService.RandomJwtToken(config.TokenSecret, 60 * 60 * 24 * 365 * 5);
        var passwordHash = CryptoService.Sha256(CryptoService.RandomToken());

        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var user = await InsertUserWithRandomPublicIdAsync(connection, transaction, name, passwordHash);
            await InsertDefaultStateAsync(connection, transaction, user.Id, user.PublicId, name);
            await UserDataService.InsertDefaultUserDataAsync(connection, transaction, user.Id, name);
            await InsertLocalLoginIdentityAsync(connection, transaction, CryptoService.Sha256(loginToken), user.Id, "GooglePlay", "", "", config.ApplicationVersion);
            await transaction.CommitAsync();
            return new RegisterGameAccountResult(loginToken, user);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<AuthResult> AuthenticateClientAsync(AuthenticatePayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.LoginToken))
        {
            throw new BadHttpRequestException(AccountErrors.InvalidLoginToken, StatusCodes.Status401Unauthorized);
        }

        var loginTokenHash = CryptoService.Sha256(payload.LoginToken);

        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var user = await FindUserByLoginTokenHashAsync(connection, transaction, loginTokenHash)
                ?? throw new BadHttpRequestException(AccountErrors.InvalidLoginToken, StatusCodes.Status401Unauthorized);
            await UpdateLocalLoginIdentityAsync(connection, transaction, loginTokenHash, payload);

            var apiToken = CryptoService.SignToken(
                new Dictionary<string, object?>
                {
                    ["jti"] = CryptoService.RandomToken(24),
                    ["uid"] = user.Id,
                    ["numericId"] = user.NumericId,
                    ["publicId"] = user.PublicId,
                    ["lc"] = "1",
                    ["pf"] = payload.GameVersion,
                    ["gv"] = payload.GameVersion,
                    ["ld"] = DateTime.UtcNow.ToString("MM/dd/yyyy HH:mm:ss")
                },
                config.TokenSecret,
                60 * 60 * 24 * 30);

            await InsertAuthSessionAsync(connection, transaction, user.Id, CryptoService.Sha256(apiToken));
            await transaction.CommitAsync();
            return new AuthResult(apiToken, user);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    static async Task<AuthUser> InsertUserWithRandomPublicIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name, string passwordHash)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var user = await TryInsertUserAsync(connection, transaction, NewPublicId(), name, passwordHash);
            if (user is not null) return user;
        }

        throw new InvalidOperationException("failed to allocate unique publicId");
    }

    static async Task<AuthUser?> TryInsertUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string publicId, string name, string passwordHash)
    {
        await using var insertCommand = new NpgsqlCommand(
            """
            insert into user_accounts (public_id, name, password_hash, role)
            values ($1, $2, $3, 'player')
            on conflict (public_id) do nothing
            returning id
            """,
            connection,
            transaction);
        insertCommand.Parameters.AddWithValue(publicId);
        insertCommand.Parameters.AddWithValue(name);
        insertCommand.Parameters.AddWithValue(passwordHash);
        var insertedId = await insertCommand.ExecuteScalarAsync();
        if (insertedId is null) return null;
        var userId = (long)insertedId;

        await using var updateCommand = new NpgsqlCommand(
            """
            update user_accounts
            set numeric_id = (100000 + (id % 900000))::integer
            where id = $1
            returning id, numeric_id, public_id, name, role
            """,
            connection,
            transaction);
        updateCommand.Parameters.AddWithValue(userId);

        await using var reader = await updateCommand.ExecuteReaderAsync();
        await reader.ReadAsync();
        return ReadAuthUser(reader);
    }

    static async Task InsertDefaultStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, string publicId, string name)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_states ("userId", state)
            values ($1, $2::jsonb)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(DefaultUserState.CreateJson(publicId, name));
        await command.ExecuteNonQueryAsync();
    }

    static async Task InsertLocalLoginIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string loginTokenHash,
        long userId,
        string gameVersion,
        string apkHash,
        string apkApplicationSignature,
        string applicationVersion)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_login_identities
            (login_token_hash, "userId", game_version, apk_hash, apk_application_signature, application_version)
            values ($1, $2, $3, $4, $5, $6)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(loginTokenHash);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(gameVersion);
        command.Parameters.AddWithValue(apkHash);
        command.Parameters.AddWithValue(apkApplicationSignature);
        command.Parameters.AddWithValue(applicationVersion);
        await command.ExecuteNonQueryAsync();
    }

    static async Task<AuthUser?> FindUserByLoginTokenHashAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string loginTokenHash)
    {
        // 与引继/密码设置保持账号行优先的锁顺序，并在锁后重新查询凭据。
        await using (var owner = new NpgsqlCommand("select \"userId\" from user_login_identities where login_token_hash=$1", connection, transaction))
        {
            owner.Parameters.AddWithValue(loginTokenHash);
            if (await owner.ExecuteScalarAsync() is not long userId) return null;
            await LockAccountAsync(connection, transaction, userId);
        }

        await using var command = new NpgsqlCommand(
            """
            select user_accounts.id, user_accounts.numeric_id, user_accounts.public_id, user_accounts.name, user_accounts.role
            from user_login_identities
            join user_accounts on user_accounts.id = user_login_identities."userId"
            where user_login_identities.login_token_hash = $1
            for update of user_login_identities
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(loginTokenHash);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadAuthUser(reader) : null;
    }

    static async Task UpdateLocalLoginIdentityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string loginTokenHash, AuthenticatePayload payload)
    {
        await using var command = new NpgsqlCommand(
            """
            update user_login_identities
            set game_version = $2,
                apk_hash = $3,
                apk_application_signature = $4,
                application_version = $5,
                last_seen_at = now()
            where login_token_hash = $1
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(loginTokenHash);
        command.Parameters.AddWithValue(payload.GameVersion);
        command.Parameters.AddWithValue(payload.ApkHash);
        command.Parameters.AddWithValue(payload.ApkApplicationSignature);
        command.Parameters.AddWithValue(payload.ApplicationVersion);
        await command.ExecuteNonQueryAsync();
    }

    static async Task InsertAuthSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, string tokenHash)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_auth_sessions ("userId", token_hash, expires_at)
            values ($1, $2, now() + interval '30 days')
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(tokenHash);
        await command.ExecuteNonQueryAsync();
    }

    static AuthUser ReadAuthUser(NpgsqlDataReader reader)
    {
        return new AuthUser(
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4));
    }

    static string NewPublicId()
    {
        return CryptoService.RandomNumericCode(10);
    }
}


