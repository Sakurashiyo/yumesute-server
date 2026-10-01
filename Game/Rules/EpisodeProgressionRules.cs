using System.Text.Json;

static class EpisodeProgressionRules
{
    public sealed record Reward(long MasterId, int Type, int Quantity);
    public static readonly IReadOnlyDictionary<long, Reward[]> Rewards = Rows("EpisodeRewardPackageMaster")
        .ToDictionary(row => row[0].GetInt64(), row => row[1].EnumerateArray()
            .Select(value => new Reward(value[0].GetInt64(), value[1].GetInt32(), value[2].GetInt32())).ToArray());
    public static readonly IReadOnlyDictionary<long, long?> Previous = Rows("EpisodeMaster")
        .ToDictionary(row => row[0].GetInt64(), row => row[6].ValueKind == JsonValueKind.Null ? (long?)null : row[6].GetInt64());
    public static readonly IReadOnlyDictionary<long, (long CharacterId, int Order)> Characters = Rows("CharacterEpisodeMaster")
        .ToDictionary(row => row[0].GetInt64(), row => (row[1].GetInt64(), row[3].GetInt32()));
    public static readonly IReadOnlyDictionary<long, int> PosterMaxPhases = Rows("PosterMaster")
        .Where(row => row[25].ValueKind != JsonValueKind.Null)
        .ToDictionary(row => row[0].GetInt64(), row => row[25].GetInt32());

    static JsonElement[] Rows(string name)
    {
        using var stream = typeof(EpisodeProgressionRules).Assembly.GetManifestResourceStream($"{(name == "PosterMaster" ? "Shop" : "Episode")}.{name}.json")
            ?? throw new InvalidOperationException($"缺少剧情主数据：{name}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}

static class EpisodeErrors
{
    public const string NotFound = "EPISODE_NOT_FOUND";
    public const string CharacterNotOwned = "EPISODE_CHARACTER_NOT_OWNED";
    public const string PreviousUnread = "EPISODE_PREVIOUS_UNREAD";
}
