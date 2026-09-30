using Npgsql;

sealed partial class UserDataService
{
    sealed record CharacterMissionState(long BaseId, long MissionId, long Count, int Received, bool HasSense)
    {
        public CharacterMissionRules.Stage[] Stages => CharacterMissionRules.ForMission(MissionId, HasSense);
        public int Cleared => Stages.Where(stage => Count >= stage.Goal).Select(stage => stage.Order).DefaultIfEmpty().Max();
        public object?[] Data(long userId, int completedLevel)
        {
            var stage = Stages.FirstOrDefault(stage => stage.Order > Received) ?? Stages.Last();
            return new object?[] { UserScopedId(userId, 960000 + BaseId * 100 + MissionId), BaseId, MissionId,
                stage.Id, Count, Cleared, Received, completedLevel };
        }
    }

    static async Task<List<CharacterMissionState>> ReadCharacterMissionStatesAsync(NpgsqlConnection connection, long userId, NpgsqlTransaction? transaction = null)
    {
        var bases = new Dictionary<long, (Dictionary<long,long> Counts, bool HasSense)>();
        await using (var command = new NpgsqlCommand("""
            select b.character_base_master_id,c.character_master_id,c.level,c.skill_level,c.extra_1
            from user_character_bases b left join user_character_cards c on c.character_base_id=b.id and c."userId"=b."userId"
            where b."userId"=$1 order by b.character_base_master_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt64(0);
                if (!bases.TryGetValue(id, out var state)) state = (new Dictionary<long,long>(), false);
                if (!reader.IsDBNull(1))
                {
                    var sense = CharacterMissionRules.HasSense(reader.GetInt64(1));
                    foreach (var (mission, count) in new[] { (3L,(long)reader.GetInt32(2)), (4L,(long)reader.GetInt32(4)), (5L,sense ? (long)reader.GetInt32(3) : 0) })
                        state.Counts[mission] = checked(state.Counts.GetValueOrDefault(mission) + count);
                    state.HasSense |= sense;
                }
                bases[id] = state;
            }
        }
        var persisted = new Dictionary<(long,long), (long Count,int Received)>();
        await using (var command = new NpgsqlCommand("""
            select character_base_master_id,mission_master_id,current_count,received_stage_order
            from user_character_mission_states where "userId"=$1
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(userId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) persisted.Add((reader.GetInt64(0),reader.GetInt64(1)), (reader.GetInt64(2),reader.GetInt32(3)));
        }
        var result = new List<CharacterMissionState>();
        foreach (var (id,state) in bases)
            foreach (var mission in CharacterMissionRules.Stages.Select(stage => stage.MissionId).Distinct())
            {
                if (CharacterMissionRules.ForMission(mission,state.HasSense).Length == 0) continue;
                var saved = persisted.GetValueOrDefault((id,mission));
                result.Add(new(id,mission,Math.Max(saved.Count,state.Counts.GetValueOrDefault(mission)),saved.Received,state.HasSense));
            }
        return result;
    }

    static Dictionary<int,int> CompletedCategories(IEnumerable<CharacterMissionState> states, bool received)
    {
        var rows = states.ToDictionary(state => state.MissionId);
        var hasSense = rows.Values.Any(state => state.HasSense);
        return CharacterMissionRules.Categories.GroupBy(category => category.Type).ToDictionary(group => group.Key, group =>
            group.Where(category =>
            {
                var applicable = CharacterMissionRules.Stages.Where(stage => stage.CategoryId == category.Id)
                    .Where(stage => hasSense || !stage.ExcludeNoSense).ToArray();
                return applicable.Length > 0 && applicable.All(stage =>
                    (received ? rows[stage.MissionId].Received : rows[stage.MissionId].Cleared) >= stage.Order);
            }).Select(category => category.Level).DefaultIfEmpty().Max());
    }

    static async Task<List<object?[]>> ReadCharacterMissionsAsync(NpgsqlConnection connection, long userId)
    {
        var states = await ReadCharacterMissionStatesAsync(connection,userId);
        var result = new List<object?[]>();
        foreach (var group in states.GroupBy(state => state.BaseId))
        {
            var categories = CompletedCategories(group,false);
            foreach (var state in group)
            {
                var category = CharacterMissionRules.Categories.Single(category => category.Id == state.Stages[0].CategoryId);
                result.Add(state.Data(userId,categories[category.Type]));
            }
        }
        return result;
    }

    public async Task<(object?[] Result,object?[] Present)> ReceiveCharacterMissionsAsync(HttpContext context, long[] baseIds, bool bulk, bool keyOnly = false)
    {
        var userId = await RequireAuthenticatedUserAsync(context);
        if (baseIds.Length is < 1 or > 100 || baseIds.Any(id => id <= 0)) throw new BadHttpRequestException(CharacterMissionErrors.InvalidRequest);
        await EnsureDefaultUserDataAsync(userId);
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // 单领与批量领取共用账号锁，跨入口并发不能重复发奖。
        await ExecuteAsync(connection,transaction,"select id from user_accounts where id=$1 for update",userId);
        var states = await ReadCharacterMissionStatesAsync(connection,userId,transaction);
        var bases = (await ReadCharacterBasesAsync(connection,userId)).ToDictionary(row => Convert.ToInt64(row[1]));
        if (baseIds.Any(id => !bases.ContainsKey(id))) throw new BadHttpRequestException(CharacterMissionErrors.CharacterMissing,404);
        var results = new List<object?>();
        var present = new List<object?>();
        var awarded = 0;
        foreach (var id in baseIds.Distinct().Order())
        {
            var before = bases[id];
            var group = states.Where(state => state.BaseId == id).ToArray();
            var updated = new List<CharacterMissionState>();
            var points = 0;
            foreach (var state in group)
            {
                if (!keyOnly)
                {
                    points = checked(points + state.Stages.Where(stage => stage.Order > state.Received && stage.Order <= state.Cleared).Sum(CharacterMissionRules.Reward));
                    var next = state with { Received = Math.Max(state.Received,state.Cleared) };
                    await ExecuteAsync(connection,transaction,"""
                    insert into user_character_mission_states("userId",character_base_master_id,mission_master_id,current_count,received_stage_order)
                    values($1,$2,$3,$4,$5) on conflict("userId",character_base_master_id,mission_master_id) do update
                    set current_count=greatest(user_character_mission_states.current_count,excluded.current_count),
                        received_stage_order=excluded.received_stage_order,updated_at=now()
                    """,userId,id,state.MissionId,state.Count,next.Received);
                    updated.Add(next);
                }
                else updated.Add(state);
            }
            var categories = CompletedCategories(updated,true);
            var keyLevel = Convert.ToInt32(before[5]);
            foreach (var key in CharacterMissionRules.Keys.Where(_ => keyOnly).OrderBy(key => key.Level))
            {
                if (key.Level <= keyLevel) continue;
                if (key.Level != keyLevel + 1 || categories.Values.Count(level => level >= key.Level) < key.RequiredCategories) break;
                points = checked(points + key.Points);
                keyLevel = key.Level;
            }
            var total = checked(Convert.ToInt32(before[3]) + points);
            awarded = checked(awarded + points);
            var rank = Math.Max(Convert.ToInt32(before[2]),CharacterMissionRules.Rank(total));
            await ExecuteAsync(connection,transaction,"""
                update user_character_bases set level=$3,exp=$4,rank=$5 where "userId"=$1 and character_base_master_id=$2
                """,userId,id,rank,total,keyLevel);
            var after = (object?[])before.Clone(); after[2]=rank; after[3]=total; after[5]=keyLevel;
            present.Add(DataObject(5,after));
            foreach (var state in updated)
            {
                var category = CharacterMissionRules.Categories.Single(category => category.Id == state.Stages[0].CategoryId);
                present.Add(DataObject(96,state.Data(userId,categories[category.Type])));
            }
            var result = new object?[] { before[2],rank,before[3],total,points,Array.Empty<object?>() };
            results.Add(bulk ? new object?[] { id,result,Array.Empty<object?>() } : result);
        }
        await transaction.CommitAsync();
        context.RequestServices.GetRequiredService<ILogger<UserDataService>>().LogInformation(
            "角色任务领取 userId={UserId} characters={Characters} awardedPoints={AwardedPoints} operation={Operation}",userId,baseIds.Distinct().Count(),awarded,keyOnly ? "key" : bulk ? "bulk" : "single");
        return (bulk ? results.ToArray() : (object?[])results.Single()!,present.ToArray());
    }
}
