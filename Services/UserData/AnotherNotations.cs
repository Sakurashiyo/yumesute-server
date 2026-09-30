using Npgsql;

sealed partial class UserDataService
{
    const int AnotherNotationUnionKey = 145;

    public async Task<LiveSettlement> FinishAnotherNotationAsync(HttpContext context, object? payload)
    {
        if (payload is not object?[] request || request.Length != 6 || request[5] is not bool auto ||
            GetLong(request, 4) is not long notationId || !PlayerProgressionRules.AnotherNotations.ContainsKey(notationId))
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidFinish);
        var finish = LiveFinishValidation.Normalize(new object?[]
        {
            0, 0, null, false, request[0], request[1], request[2], request[3], null, null
        });
        var judges = ReadFinishJudges(finish);
        var achievement = PlayerProgressionRules.Achievement(judges);
        var cleared = finish[3] is true;
        var lamp = !auto && cleared ? judges.Where(pair => pair.Key < 5).Sum(pair => pair.Value) == 0 ? 6 :
            judges.Where(pair => pair.Key <= 2).Sum(pair => pair.Value) == 0 ? 2 : 1 : 0;
        var grade = auto ? 0 : PlayerProgressionRules.Grade(achievement);
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException("ACCOUNT_UNAUTHORIZED", 401);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        decimal before = 0, best = 0;
        var beforeLamp = 0;
        var present = Array.Empty<object?>();
        if (!auto)
        {
            // 只保存最高成绩，没有奖励或次数累加，重试与并发提交天然幂等。
            await ExecuteAsync(connection, transaction, """
                insert into user_another_notation_results (id, "userId", another_notation_master_id)
                values ($1, $2, $3) on conflict ("userId", another_notation_master_id) do nothing
                """, UserScopedId(userId, 1450000 + notationId), userId, notationId);
            await using (var previous = new NpgsqlCommand("""
                select achievement_rate, clear_lamp from user_another_notation_results
                where "userId" = $1 and another_notation_master_id = $2 for update
                """, connection, transaction))
            {
                previous.Parameters.AddWithValue(userId);
                previous.Parameters.AddWithValue(notationId);
                await using var reader = await previous.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) throw new InvalidOperationException("缺少特殊谱面成绩行");
                before = reader.GetDecimal(0);
                beforeLamp = reader.GetInt32(1);
            }
            best = Math.Max(before, achievement);
            await ExecuteAsync(connection, transaction, """
                update user_another_notation_results set achievement_rate = greatest(achievement_rate, $3),
                  clear_lamp = greatest(clear_lamp, $4), rate_grade = greatest(rate_grade, $5)
                where "userId" = $1 and another_notation_master_id = $2
                """, userId, notationId, achievement, lamp, grade);
            var row = (await ReadAnotherNotationResultsAsync(connection, userId))
                .Single(value => Convert.ToInt64(value[1]) == notationId);
            present = new object?[] { DataObject(AnotherNotationUnionKey, row) };
        }
        // ヤネウラ无需 Start 会话，独立谱面不会影响普通歌曲的 rating、体力和掉落。
        var result = new object?[31];
        result[1] = Array.Empty<object?>();
        var rating = await ReadPlayerRatingAsync(connection, userId);
        result[2] = new object?[] { new object?[] { (double)before, (double)achievement }, new object?[] { 0.0, 0.0 }, rating, rating };
        result[5] = lamp; result[6] = grade; result[8] = !auto && achievement > before;
        result[9] = 0; result[10] = 0; result[11] = Array.Empty<object?>(); result[12] = Array.Empty<object?>();
        result[13] = (double)achievement; result[20] = beforeLamp; result[21] = 0;
        result[23] = Array.Empty<object?>(); result[30] = Array.Empty<object?>();
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "特殊谱面结算 userId={UserId} notationId={NotationId} auto={Auto} cleared={Cleared} achievement={Achievement} best={Best}",
            userId, notationId, auto, cleared, achievement, best);
        return new(result, present);
    }

    static async Task<List<object?[]>> ReadAnotherNotationResultsAsync(NpgsqlConnection connection, long userId)
    {
        await using var command = new NpgsqlCommand("""
            select id, another_notation_master_id, clear_lamp, rate_grade, achievement_rate
            from user_another_notation_results where "userId" = $1 order by another_notation_master_id
            """, connection);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
            rows.Add(new object?[] { reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetDecimal(4).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        return rows;
    }
}
