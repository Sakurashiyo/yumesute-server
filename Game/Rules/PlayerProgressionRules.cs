using System.Text.Json;

// 主数据规则与持久化分离，便于验证跨级、上限和成绩边界。
static class PlayerProgressionRules
{
    public sealed record RankRule(int Rank, int RequiredExp, int MaxStamina);
    public sealed record Chart(long Id, int Difficulty, int Level, long MusicMasterId);
    public sealed record RankProgress(int Rank, int Exp, int MaxStamina, int StaminaRecovery);
    static readonly Dictionary<int, RankRule> Ranks = ReadTable("PlayerRankMaster")
        .ToDictionary(row => row[0].GetInt32(), row => new RankRule(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32()));
    public static readonly IReadOnlyDictionary<long, Chart> Charts = ReadTable("LiveMaster")
        .ToDictionary(row => row[0].GetInt64(), row => new Chart(row[0].GetInt64(), row[1].GetInt32(), row[3].GetInt32(), row[2].GetInt64()));
    public static readonly IReadOnlyDictionary<long, Chart> AnotherNotations = ReadTable("AnotherNotationMaster")
        .ToDictionary(row => row[0].GetInt64(), row => new Chart(row[0].GetInt64(), row[4].GetInt32(), row[5].GetInt32(), row[1].GetInt64()));

    static JsonElement[] ReadTable(string name)
    {
        using var stream = typeof(PlayerProgressionRules).Assembly.GetManifestResourceStream($"Progression.{name}.json")
            ?? throw new InvalidOperationException($"缺少结算主数据：{name}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    public static RankProgress Advance(int rank, int exp, int gain, int limit)
    {
        if (!Ranks.ContainsKey(rank) || exp < 0 || gain < 0 || limit < rank || !Ranks.ContainsKey(limit))
            throw new InvalidOperationException("玩家等级或经验状态非法");
        var remaining = checked((long)exp + gain);
        var recovery = 0;
        while (rank < limit && remaining >= Ranks[rank].RequiredExp)
        {
            if (Ranks[rank].RequiredExp <= 0) throw new InvalidOperationException("升级门槛必须为正数");
            remaining -= Ranks[rank].RequiredExp;
            rank++;
            recovery = checked(recovery + Ranks[rank].MaxStamina);
        }
        if (rank == limit) remaining = Math.Min(remaining, Ranks[rank].RequiredExp - 1L);
        return new(rank, checked((int)remaining), Ranks[rank].MaxStamina, recovery);
    }

    public static decimal Achievement(IReadOnlyDictionary<int, int> judges)
    {
        if (judges.Any(pair => pair.Key is < 0 or > 6 || pair.Value < 0 || pair.Key == 0 && pair.Value != 0))
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidJudges);
        var count = judges.Values.Sum(value => (long)value);
        if (count <= 0) throw new BadHttpRequestException(LiveProgressionErrors.InvalidJudges);
        var points = judges.Sum(pair => pair.Value * (pair.Key switch { 6 => 101m, 5 => 100m, 4 => 80m, 3 => 50m, _ => 0m }));
        return points / count;
    }

    // 按用户指定的 Gamerch 计算规格实现：https://gamerch.com/world-dai-star/786842。
    public static decimal NotationRate(int level, decimal achievement)
    {
        if (level <= 0 || achievement is < 0 or > 101) throw new ArgumentOutOfRangeException(nameof(achievement));
        var rate = achievement switch
        {
            < 80m => 0m,
            < 90m => level * achievement / 180m,
            < 95m => level * (0.5m + (achievement - 90m) / 20m),
            < 98m => level * (0.75m + (achievement - 95m) / 12m),
            _ => level + Bonus(achievement)
        };
        return Math.Floor(rate * 100m) / 100m;
    }

    static decimal Bonus(decimal achievement) => achievement switch
        {
            >= 100.95m => 6m + (achievement - 100.95m),
            >= 100.75m => 4.5m + (achievement - 100.75m) * 7.5m,
            >= 100.5m => 3m + (achievement - 100.5m) * 6m,
            >= 100.25m => 2.25m + (achievement - 100.25m) * 3m,
            >= 100m => 1.5m + (achievement - 100m) * 3m,
            _ => (achievement - 98m) * 0.75m
        };

    public static int Grade(decimal achievement) => achievement switch
    {
        >= 100.95m => 9, >= 100.75m => 8, >= 100.5m => 7, >= 100.25m => 6,
        >= 100m => 5, >= 98m => 4, >= 95m => 3, >= 90m => 2, >= 80m => 1, _ => 0
    };
}

static class LiveProgressionErrors
{
    public const string InvalidStart = "LIVE_INVALID_START";
    public const string InvalidFinish = "LIVE_INVALID_FINISH";
    public const string InvalidJudges = "LIVE_INVALID_JUDGES";
    public const string SessionMissing = "LIVE_SESSION_MISSING";
    public const string SessionConflict = "LIVE_SESSION_CONFLICT";
}
