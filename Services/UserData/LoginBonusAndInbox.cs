using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
    public async Task<LoginBonusReceiveResult> CheckReceiveLoginBonusAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? await FindLatestUserIdAsync();
        if (userId is null) return new LoginBonusReceiveResult(Array.Empty<object?>(), Array.Empty<object?>());

        await EnsureDefaultUserDataAsync(userId.Value);

        var now = DateTimeOffset.UtcNow;
        var activeRewards = LoginBonusRewards
            .Where(reward => reward.StartAt <= now && now < reward.EndAt)
            .ToArray();
        if (activeRewards.Length == 0)
        {
            return new LoginBonusReceiveResult(Array.Empty<object?>(), Array.Empty<object?>());
        }

        var windowStartUtc = CurrentDailyResetBoundaryUtc(now).UtcDateTime;
        var windowKey = new DateTimeOffset(windowStartUtc).ToOffset(JapanStandardTimeOffset).ToString("yyyyMMdd");

        await using var connection = await database.OpenConnectionAsync();

        var alreadyReceived = false;
        await using (var command = new NpgsqlCommand(
            """
            select shown_at, status
            from user_login_bonus_states
            where "userId" = $1
            """,
            connection))
        {
            command.Parameters.AddWithValue(userId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var shownAt = reader.GetDateTime(0);
                var status = reader.GetInt32(1);
                alreadyReceived = status == 1 && shownAt >= windowStartUtc;
            }
        }

        if (alreadyReceived)
        {
            return new LoginBonusReceiveResult(Array.Empty<object?>(), Array.Empty<object?>());
        }

        var bootstrapKeys = activeRewards.Select(reward => $"login-bonus-{windowKey}-{reward.Id}").ToArray();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            foreach (var reward in activeRewards)
            {
                var bootstrapKey = $"login-bonus-{windowKey}-{reward.Id}";
                var payload = JsonSerializer.Serialize(new
                {
                    bootstrapKey,
                    ThingType = reward.ThingType,
                    MasterId = reward.MasterId,
                    Quantity = reward.Quantity,
                    Message = $"{reward.Title}の報酬です"
                });

                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    insert into user_item_inbox_packages ("userId", payload, expires_at)
                    select $1, $2::jsonb, now() + interval '30 days'
                    where not exists (
                      select 1
                      from user_item_inbox_packages
                      where "userId" = $1
                        and payload ->> 'bootstrapKey' = $3
                    )
                    """,
                    userId.Value,
                    payload,
                    bootstrapKey);
            }

            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into user_login_bonus_states (id, "userId", current_count, total_count, shown_at, status)
                values ($1, $2, 1, 1, now(), 1)
                on conflict ("userId") do update set
                  current_count = user_login_bonus_states.current_count + 1,
                  total_count = user_login_bonus_states.total_count + 1,
                  shown_at = now(),
                  status = 1,
                  updated_at = now()
                """,
                UserScopedId(userId.Value, 10901),
                userId.Value);

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }

        var packages = await ReadInboxPackagesByBootstrapKeysAsync(connection, userId.Value, bootstrapKeys);
        var presentData = packages.Select(value => DataObject(41, value)).ToArray();
        return new LoginBonusReceiveResult(
            activeRewards.Select(BuildLoginBonusResult).ToArray<object?>(),
            presentData);
    }

    public async Task<bool> HasUnreadInboxPackagesAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return false;

        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select exists (
              select 1
              from user_item_inbox_packages
              where "userId" = $1
                and received_at is null
                and (expires_at is null or expires_at > now())
            )
            """,
            connection);
        command.Parameters.AddWithValue(userId.Value);
        return await command.ExecuteScalarAsync() is true;
    }

    public async Task<InboxCheckPackagesResult> CheckInboxPackagesAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return new InboxCheckPackagesResult(false, Array.Empty<object?>());

        // 该接口只检查现有收件箱状态，不应在读取操作中创建测试礼包。
        return new InboxCheckPackagesResult(true, Array.Empty<object?>());
    }

    public async Task<InboxReceivePackagesResult> ReceiveInboxPackagesAsync(HttpContext context, IReadOnlyCollection<long>? inboxIds = null)
    {
        var userId = await GetCurrentUserIdAsync(context);
        if (userId is null) return new InboxReceivePackagesResult(Array.Empty<object?>(), false, Array.Empty<object?>());

        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var hasIds = inboxIds is { Count: > 0 };
        var packages = new List<InboxPackageRecord>();
        await using (var command = new NpgsqlCommand(
            hasIds
                ? """
                  select id, payload::text, created_at, expires_at
                  from user_item_inbox_packages
                  where "userId" = $1
                    and id = any($2)
                    and received_at is null
                    and (expires_at is null or expires_at > now())
                  order by id
                  """
                : """
                  select id, payload::text, created_at, expires_at
                  from user_item_inbox_packages
                  where "userId" = $1
                    and received_at is null
                    and (expires_at is null or expires_at > now())
                  order by id
                  """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(userId.Value);
            if (hasIds) command.Parameters.AddWithValue(inboxIds!.ToArray());
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                packages.Add(ReadInboxPackageRecord(reader));
            }
        }

        var receivedAt = DateTime.UtcNow;
        var rewards = new List<object?>();
        var presentData = new List<object?>();
        var shouldReadCurrency = false;

        if (packages.Count > 0)
        {
            await using (var command = new NpgsqlCommand(
                """
                update user_item_inbox_packages
                set received_at = $3,
                    updated_at = now()
                where "userId" = $1
                  and id = any($2)
                  and received_at is null
                """,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(userId.Value);
                command.Parameters.AddWithValue(packages.Select(package => package.Id).ToArray());
                command.Parameters.AddWithValue(receivedAt);
                await command.ExecuteNonQueryAsync();
            }

            foreach (var package in packages)
            {
                rewards.Add(BuildRewardResult(package.ThingType, package.MasterId, package.Quantity));
                presentData.Add(DataObject(41, BuildInboxPackageRow(package, receivedAt)));

                switch (package.ThingType)
                {
                    case 1:
                        var itemPossession = await AddItemPossessionAsync(connection, transaction, userId.Value, package.MasterId, package.Quantity);
                        presentData.Add(DataObject(27, itemPossession));
                        break;
                    case 12:
                        await AddCoinsAsync(connection, transaction, userId.Value, package.Quantity);
                        shouldReadCurrency = true;
                        break;
                    case 13:
                        await AddFreeJewelsAsync(connection, transaction, userId.Value, package.Quantity);
                        shouldReadCurrency = true;
                        break;
                }
            }

            if (shouldReadCurrency)
            {
                presentData.Add(DataObject(128, await ReadCurrencyAsync(connection, transaction, userId.Value)));
            }
        }

        var hasUnread = false;
        await using (var command = new NpgsqlCommand(
            """
            select exists (
              select 1
              from user_item_inbox_packages
              where "userId" = $1
                and received_at is null
                and (expires_at is null or expires_at > now())
            )
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(userId.Value);
            hasUnread = await command.ExecuteScalarAsync() is true;
        }

        await transaction.CommitAsync();
        return new InboxReceivePackagesResult(rewards.ToArray(), hasUnread, presentData.ToArray());
    }

    static InboxPackageRecord ReadInboxPackageRecord(NpgsqlDataReader reader)
    {
        using var document = JsonDocument.Parse(reader.GetString(1));
        var payload = document.RootElement;
        var thingType = payload.TryGetProperty("ThingType", out var thingTypeElement) ? thingTypeElement.GetInt32() : 1;
        var masterId = payload.TryGetProperty("MasterId", out var masterIdElement) ? masterIdElement.GetInt64() : 0L;
        var quantity = payload.TryGetProperty("Quantity", out var quantityElement) ? quantityElement.GetInt32() : 1;
        var message = payload.TryGetProperty("Message", out var messageElement) ? messageElement.GetString() ?? "" : "";
        return new InboxPackageRecord(
            reader.GetInt64(0),
            thingType,
            masterId,
            quantity,
            message,
            reader.GetDateTime(2),
            reader.IsDBNull(3) ? null : reader.GetDateTime(3));
    }

    static object?[] BuildInboxPackageRow(InboxPackageRecord package, DateTime? receivedAt)
    {
        return new object?[]
        {
            package.Id,
            package.ThingType,
            package.MasterId,
            package.Quantity,
            true,
            receivedAt is not null,
            null,
            package.Message,
            package.CreatedAt,
            receivedAt,
            package.ExpiresAt
        };
    }

    static object?[] BuildRewardResult(int thingType, long masterId, int quantity)
    {
        return new object?[] { thingType, masterId, quantity, null, null, null, false };
    }

    static async Task AddCoinsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        int amount)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into user_item_currencies ("userId", free_jewel, paid_jewel, coin)
            values ($1, 0, 0, $2)
            on conflict ("userId")
            do update set coin = user_item_currencies.coin + excluded.coin
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(amount);
        await command.ExecuteNonQueryAsync();
    }

    static async Task<List<object?[]>> ReadInboxPackagesAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, payload::text, created_at, received_at, expires_at
            from user_item_inbox_packages
            where "userId" = $1
              and (expires_at is null or expires_at > now())
            order by id
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            using var document = JsonDocument.Parse(reader.GetString(1));
            var payload = document.RootElement;
            var thingType = payload.TryGetProperty("ThingType", out var thingTypeElement) ? thingTypeElement.GetInt32() : 1;
            var masterId = payload.TryGetProperty("MasterId", out var masterIdElement) ? masterIdElement.GetInt64() : 0L;
            var quantity = payload.TryGetProperty("Quantity", out var quantityElement) ? quantityElement.GetInt32() : 1;
            var message = payload.TryGetProperty("Message", out var messageElement) ? messageElement.GetString() ?? "" : "";
            var package = new InboxPackageRecord(
                reader.GetInt64(0),
                thingType,
                masterId,
                quantity,
                message,
                reader.GetDateTime(2),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4));
            result.Add(BuildInboxPackageRow(package, reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
        }
        return result;
    }

    static async Task<List<object?[]>> ReadInboxPackagesByBootstrapKeysAsync(NpgsqlConnection connection, long userId, IReadOnlyCollection<string> bootstrapKeys)
    {
        if (bootstrapKeys.Count == 0) return new List<object?[]>();

        await using var command = new NpgsqlCommand(
            """
            select id, payload::text, created_at, expires_at
            from user_item_inbox_packages
            where "userId" = $1
              and payload ->> 'bootstrapKey' = any($2)
              and received_at is null
              and (expires_at is null or expires_at > now())
            order by id
            """,
            connection);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(bootstrapKeys.ToArray());

        var result = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            using var document = JsonDocument.Parse(reader.GetString(1));
            var payload = document.RootElement;
            var thingType = payload.TryGetProperty("ThingType", out var thingTypeElement) ? thingTypeElement.GetInt32() : 1;
            var masterId = payload.TryGetProperty("MasterId", out var masterIdElement) ? masterIdElement.GetInt64() : 0L;
            var quantity = payload.TryGetProperty("Quantity", out var quantityElement) ? quantityElement.GetInt32() : 1;
            var message = payload.TryGetProperty("Message", out var messageElement) ? messageElement.GetString() ?? "" : "";
            result.Add(new object?[]
            {
                reader.GetInt64(0),
                thingType,
                masterId,
                quantity,
                true,
                false,
                null,
                message,
                reader.GetDateTime(2),
                null,
                reader.IsDBNull(3) ? null : reader.GetDateTime(3)
            });
        }
        return result;
    }

    static async Task<object?[]> ReadLoginBonusStateAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand(
            """
            select id, current_count, total_count, shown_at, status
            from user_login_bonus_states
            where "userId" = $1
            """,
            connection);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new object?[] { UserScopedId(userId, 10901), 0, 0, DateTime.UtcNow, 0 };
        return new object?[]
        {
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetDateTime(3),
            reader.GetInt32(4)
        };
    }

    static object?[] BuildLoginBonusResult(LoginBonusReward reward)
    {
        var dailyRewards = Enumerable.Range(1, reward.Days)
            .Select(day => new object?[] { reward.ThingType, reward.MasterId, reward.Quantity, day })
            .ToArray();

        return new object?[]
        {
            reward.Id,
            1,
            new object?[] { new object?[] { reward.ThingType, reward.MasterId, reward.Quantity, null, null, null, false } },
            reward.Title,
            1,
            reward.StartAt.UtcDateTime,
            reward.EndAt.UtcDateTime,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            reward.Days,
            reward.IsLoop,
            reward.DisplayOrder,
            dailyRewards,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            reward.DisplayCategory,
            null,
            null
        };
    }
}
