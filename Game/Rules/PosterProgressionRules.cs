using System.Text.Json;

static class PosterProgressionRules
{
    public sealed record Costume(int Phase, long Id);
    public sealed record Poster(long Id, int Rarity, long LevelPattern, int MaxPhase, Costume[] Costumes);
    public sealed record Cost(int Level, long ItemId, int Quantity);
    public static readonly IReadOnlyDictionary<long, Poster> Posters = GachaRules.ReadRows("Shop.PosterMaster.json")
        .Cast<object?[]>().ToDictionary(row => GachaRules.Long(row[0]), row => new Poster(
            GachaRules.Long(row[0]), (int)GachaRules.Long(row[3]), GachaRules.Long(row[5]), (int)GachaRules.Long(row[25]),
            ((object?[])row[16]!).Cast<object?[]>().Where(costume => costume[1] is not null)
                .Select(costume => new Costume((int)GachaRules.Long(costume[0]), GachaRules.Long(costume[1]))).ToArray()));
    public static readonly IReadOnlyDictionary<long, Cost[]> Patterns = ReadPatterns();

    static Dictionary<long, Cost[]> ReadPatterns()
    {
        using var stream = typeof(PosterProgressionRules).Assembly.GetManifestResourceStream("Progression.PosterLevelPatternGroupMaster.json")
            ?? throw new InvalidOperationException("缺少海报升级主数据");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().ToDictionary(row => row[0].GetInt64(),
            row => row[1].EnumerateArray().Select(cost => new Cost(cost[2].GetInt32(), cost[3].GetInt64(), cost[4].GetInt32())).ToArray());
    }

    // 与客户端 Poster.LevelLimit 一致，第五次及以后的突破每次增加两级。
    public static int LevelLimit(Poster poster, int phase) =>
        checked(poster.Rarity * 2 + (phase <= 4 ? phase : phase * 2 - 4));

    public static Dictionary<long, int> LevelCosts(Poster poster, int from, int to) => Patterns[poster.LevelPattern]
        .Where(cost => cost.Level >= from && cost.Level < to).GroupBy(cost => cost.ItemId)
        .ToDictionary(group => group.Key, group => checked(group.Sum(cost => cost.Quantity)));
}

static class PosterProgressionErrors
{
    public const string Invalid = "POSTER_INVALID_REQUEST";
    public const string NotOwned = "POSTER_NOT_OWNED";
    public const string LevelLimit = "POSTER_LEVEL_LIMIT";
    public const string InsufficientItems = "POSTER_INSUFFICIENT_ITEMS";
}
