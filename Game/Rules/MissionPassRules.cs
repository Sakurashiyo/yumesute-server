static class MissionPassRules
{
    public const int ExchangePointPerUnit = 400;
    public const int ExchangeJewelPerUnit = 200;
    public const int MaxPoints = 9999999;
    public const int NotReached = 4;
    public const int Received = 2;
    public const int Success = 5;
    public static readonly DateTime PermanentEnd = new(2099, 12, 31, 0, 0, 0, DateTimeKind.Utc);
    static readonly object?[] Pass = GachaRules.ReadRows("Mission.MissionPassMaster.json").Cast<object?[]>()
        .MaxBy(row => (DateTime)row[2]!)!;
    public static long Id => GachaRules.Long(Pass[0]);
    public static long PointItemId => GachaRules.Long(Pass[1]);
    public static DateTime Start => (DateTime)Pass[2]!;
    static DateTime OriginalEnd => (DateTime)Pass[3]!;
    public sealed record Reward(int Type, long MasterId, int Quantity, bool Sp);
    public sealed record Stage(int Phase, int Points, Reward[] Rewards);
    public static readonly Stage[] Stages = LoadStages();
    public static readonly Reward[][] LoopRewards = GachaRules.ReadRows("Mission.MissionPassLoopRewardMaster.json")
        .Cast<object?[]>().Where(row => (DateTime)row[2]! == Start && (DateTime)row[3]! == OriginalEnd)
        .OrderBy(row => GachaRules.Long(row[1])).Select(row => ((object?[])row[4]!).Cast<object?[]>()
            .Select(reward => new Reward(Convert.ToInt32(reward[3]), GachaRules.Long(reward[2]),
                Convert.ToInt32(reward[4]), (bool)reward[5]!)).ToArray()).ToArray();

    static Stage[] LoadStages()
    {
        var stages = GachaRules.ReadRows("Mission.MissionPassDetailMaster.json").Cast<object?[]>()
            .Where(row => row[2] is null
                ? (DateTime)row[4]! <= Start && (DateTime)row[5]! > Start
                : GachaRules.Long(row[2]) == Id)
            .GroupBy(row => Convert.ToInt32(row[1]))
            .Select(group => group.OrderByDescending(row => row[2] is not null).First())
            .OrderBy(row => GachaRules.Long(row[1])).Select(row => new Stage(
                Convert.ToInt32(row[1]), Convert.ToInt32(row[3]), ((object?[])row[6]!).Cast<object?[]>()
                    .Select(reward => new Reward(Convert.ToInt32(reward[2]), GachaRules.Long(reward[1]),
                        Convert.ToInt32(reward[3]), (bool)reward[5]!)).ToArray())).ToArray();
        if (stages.Length != 50 || !stages.Select(stage => stage.Phase).SequenceEqual(Enumerable.Range(1, 50))
            || stages.Where((stage, index) => stage.Points != (index + 1) * 400).Any())
            throw new InvalidDataException("Mission Pass 阶段主数据不完整");
        return stages;
    }

    public static bool IsOpen(long id, DateTime now) => id == Id && now >= Start && now < PermanentEnd;
    // 客户端 Constants.Mission：循环 1/6/11/31 起每阶段分别需要 400/800/2400/4800 积分。
    public static int LoopStep(int cycle) => cycle >= 31 ? 4800 : cycle >= 11 ? 2400 : cycle >= 6 ? 800 : 400;
    public static int ReachedLoops(int points)
    {
        if (LoopRewards.Length != 4) throw new InvalidDataException("Mission Pass 循环奖励主数据不完整");
        long threshold = 20000;
        var reached = 0;
        for (var cycle = 1; ; cycle++)
            for (var phase = 0; phase < LoopRewards.Length; phase++)
            {
                threshold = checked(threshold + LoopStep(cycle));
                if (points < threshold) return reached;
                reached++;
            }
    }

    public static object?[] MasterPasses(object?[] rows, MissionPassSettings settings) => rows.Cast<object?[]>().Select(row =>
    {
        if (GachaRules.Long(row[0]) != Id) return (object?)row;
        var copy = (object?[])row.Clone(); copy[2] = settings.StartsAt.UtcDateTime;
        copy[3] = settings.Enabled ? (settings.EndsAt?.UtcDateTime ?? PermanentEnd) : settings.StartsAt.UtcDateTime;
        return copy;
    }).ToArray();
    public static object?[] MasterStages(object?[] rows, MissionPassSettings settings) =>
        rows.Cast<object?[]>().Where(row => row[2] is null || GachaRules.Long(row[2]) != Id).Cast<object?>()
            .Concat(settings.Stages.Select(stage =>
            {
                var detailId = Id * 100 + stage.Phase;
                return (object?)new object?[] { detailId, stage.Phase, Id, stage.Points,
                    settings.StartsAt.UtcDateTime, settings.EndsAt?.UtcDateTime ?? PermanentEnd,
                    stage.Rewards.Select((reward, index) => (object?)new object?[]
                    { detailId * 100 + index + 1, reward.MasterId, reward.Type, reward.Quantity, detailId, reward.Sp }).ToArray() };
            })).ToArray();
    public static object?[] MasterLoops(object?[] rows, MissionPassSettings settings) => rows.Cast<object?[]>().Select(row =>
    {
        if ((DateTime)row[2]! != Start || (DateTime)row[3]! != OriginalEnd) return (object?)row;
        var copy = (object?[])row.Clone(); copy[2] = settings.StartsAt.UtcDateTime;
        copy[3] = settings.Enabled ? (settings.EndsAt?.UtcDateTime ?? PermanentEnd) : settings.StartsAt.UtcDateTime;
        copy[4] = settings.LoopRewards[Convert.ToInt32(row[1]) - 1].Select((reward, index) =>
            (object?)new object?[] { GachaRules.Long(row[0]) * 100 + index + 1, row[0], reward.MasterId,
                reward.Type, reward.Quantity, reward.Sp }).ToArray();
        return copy;
    }).ToArray();
}

static class MissionPassErrors
{
    public const string Unavailable = "MISSION_PASS_UNAVAILABLE";
    public const string Invalid = "MISSION_PASS_INVALID_REQUEST";
    public const string Insufficient = "MISSION_PASS_INSUFFICIENT_JEWELS";
    public const string PointLimit = "MISSION_PASS_POINT_LIMIT";
}
