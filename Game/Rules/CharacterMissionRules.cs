using System.Text.Json;

static class CharacterMissionRules
{
    public sealed record Stage(long Id, long CategoryId, long MissionId, bool ExcludeNoSense, int Order, long Goal);
    public sealed record Category(long Id, int Type, int Level, int Points);
    public sealed record KeyMission(int Level, int Points, int RequiredCategories);
    public static readonly Category[] Categories = Read("CharacterMissionCategoryLevelMaster", row => new Category(row[0].GetInt64(), row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32()));
    public static readonly Stage[] Stages = Read("CharacterMissionStageMaster", row => new Stage(row[0].GetInt64(), row[1].GetInt64(), row[2].GetInt64(), row[3].GetBoolean(), row[5].GetInt32(), row[6].GetInt64()));
    public static readonly KeyMission[] Keys = Read("CharacterKeyMissionMaster", row => new KeyMission(row[1].GetInt32(), row[3].GetInt32(), row[6].GetInt32()));
    static readonly long[] RankPoints = Read("CharacterStarRankMaster", row => row[1].GetInt64());
    static readonly Dictionary<long, bool> SenseCharacters = ReadCharacters();

    public static bool HasSense(long masterId) => SenseCharacters[masterId];
    public static Stage[] ForMission(long id, bool hasSense) => Stages.Where(stage => stage.MissionId == id && (hasSense || !stage.ExcludeNoSense)).OrderBy(stage => stage.Order).ToArray();
    public static int Reward(Stage stage)
    {
        var category = Categories.Single(category => category.Id == stage.CategoryId);
        var stages = Stages.Where(row => row.CategoryId == stage.CategoryId).OrderBy(row => row.Order).ToArray();
        var index = Array.FindIndex(stages, row => row.Id == stage.Id);
        if (index < 0) throw new InvalidOperationException("角色任务阶段与类别不一致");
        return category.Points * (index + 1) / stages.Length - category.Points * index / stages.Length;
    }
    public static int Rank(int total)
    {
        long remaining = total;
        var rank = 0;
        while (rank < RankPoints.Length - 1 && remaining >= RankPoints[rank]) remaining -= RankPoints[rank++];
        return rank;
    }
    static Dictionary<long, bool> ReadCharacters()
    {
        using var stream = typeof(CharacterMissionRules).Assembly.GetManifestResourceStream("AdminMaster.CharacterMaster.json")
            ?? throw new InvalidOperationException("缺少角色主数据");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().ToDictionary(row => row[0].GetInt64(), row => row[11].GetInt64() != 0);
    }
    static T[] Read<T>(string name, Func<JsonElement,T> map)
    {
        using var stream = typeof(CharacterMissionRules).Assembly.GetManifestResourceStream($"CharacterMission.{name}.json")
            ?? throw new InvalidOperationException($"缺少角色任务主数据：{name}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(map).ToArray();
    }
}

static class CharacterMissionErrors
{
    public const string InvalidRequest = "CHARACTER_MISSION_INVALID_REQUEST";
    public const string CharacterMissing = "CHARACTER_MISSION_CHARACTER_MISSING";
}
