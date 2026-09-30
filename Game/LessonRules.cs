using System.Text.Json;

static class LessonRules
{
    public sealed record Reward(int Type, long MasterId, int Quantity);
    public sealed record Milestone(long Score, Reward[] Rewards);
    public static readonly IReadOnlyDictionary<long, Milestone[]> Milestones = Load();

    static JsonElement[] Read(string name)
    {
        using var stream = typeof(LessonRules).Assembly.GetManifestResourceStream($"Lesson.{name}.json")
            ?? throw new InvalidOperationException($"缺少稽古主数据：{name}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    static IReadOnlyDictionary<long, Milestone[]> Load()
    {
        var groups = Read("LessonScoreRewardGroupMaster").ToDictionary(row => row[0].GetInt64(), row =>
            row[1].EnumerateArray().Select(reward => new Reward(reward[1].GetInt32(), reward[2].GetInt64(), reward[3].GetInt32())).ToArray());
        return Read("CharacterLessonScoreRewardMaster").GroupBy(row => row[1].GetInt64()).ToDictionary(
            group => group.Key,
            group => group.Select(row => new Milestone(row[2].GetInt64(), groups[row[3].GetInt64()])).OrderBy(row => row.Score).ToArray());
    }
}

static class LessonErrors
{
    public const string InvalidStart = "LESSON_INVALID_START";
    public const string InvalidFinish = "LESSON_INVALID_FINISH";
    public const string DailyLimitReached = "LESSON_DAILY_LIMIT_REACHED";
    public const string SessionMissing = "LESSON_SESSION_MISSING";
    public const string SessionConflict = "LESSON_SESSION_CONFLICT";
}
