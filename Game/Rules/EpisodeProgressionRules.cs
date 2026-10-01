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

    public sealed record Summary(string Title, int StoryType, int Order);
    public static readonly IReadOnlyDictionary<long, Summary> Summaries = BuildSummaries();

    static IReadOnlyDictionary<long, Summary> BuildSummaries()
    {
        var result = new Dictionary<long, Summary>();
        var storyTypes = Rows("StoryMaster").ToDictionary(row => row[0].GetInt64(), row => row[1].GetInt32());
        var characterNames = Rows("CharacterMaster").ToDictionary(row => row[0].GetInt64(), row => row[2].GetString()!);
        foreach (var row in Rows("EpisodeMaster"))
            result.Add(row[0].GetInt64(), new(row[2].GetString()!, storyTypes[row[1].GetInt64()], row[3].GetInt32()));
        // 联动活动也可能出现在主线列表，已有 EpisodeMaster 定义优先，避免误走活动剧情分支。
        foreach (var row in Rows("StoryEventEpisodeMaster"))
            result.TryAdd(row[0].GetInt64(), new(row[4].GetString()!, 2, row[5].GetInt32()));
        foreach (var row in Rows("CharacterEpisodeMaster"))
            result.Add(row[0].GetInt64(), new(characterNames[row[1].GetInt64()], 3, row[3].GetInt32()));
        foreach (var row in Rows("SpotConversationMaster"))
            result.Add(row[9].GetInt64(), new(row[15].GetString()!, 4, 1));
        foreach (var row in Rows("SpecialEpisodeMaster"))
            result.Add(row[0].GetInt64(), new(row[2].GetString()!, 5, 1));
        if (Rewards.Keys.Any(id => !result.ContainsKey(id)))
            throw new InvalidOperationException("剧情奖励主数据存在缺少类型定义的剧情");
        return result;
    }

    static JsonElement[] Rows(string name)
    {
        var group = name switch { "PosterMaster" => "Shop", "CharacterMaster" => "AdminMaster", _ => "Episode" };
        using var stream = typeof(EpisodeProgressionRules).Assembly.GetManifestResourceStream($"{group}.{name}.json")
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
