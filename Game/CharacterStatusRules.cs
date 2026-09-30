using System.Text.Json;

static class CharacterStatusRules
{
    sealed record Rank(int Level, long RequiredScore, float Bonus);
    static readonly Rank[] Ranks = Read("CharacterMission.CharacterStarRankMaster").Select(row =>
        new Rank(row[0].GetInt32(), row[2].GetInt64(), row[3].GetSingle())).ToArray();
    static readonly Dictionary<long, JsonElement> Cards = Read("AdminMaster.CharacterMaster").ToDictionary(row => row[0].GetInt64());
    static readonly Dictionary<int, int> Levels = Read("Progression.CharacterLevelMaster").ToDictionary(row => row[0].GetInt32(), row => row[2].GetInt32());
    static readonly Dictionary<long, JsonElement> Blooms = Read("Status.CharacterBloomBonusGroupMaster").ToDictionary(row => row[0].GetInt64());
    static readonly Dictionary<long, JsonElement> Effects = Read("Status.EffectMaster").ToDictionary(row => row[0].GetInt64());

    public static float BonusLimit(long bestScore)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bestScore);
        return Ranks.Where(rank => rank.RequiredScore <= bestScore).Max(rank => rank.Bonus);
    }

    public static float Bonus(int starRank, long bestScore) =>
        Math.Min(Ranks.Single(rank => rank.Level == starRank).Bonus, BonusLimit(bestScore));

    public static object?[] Calculate(object?[] card, int starRank, long bestScore)
    {
        var master = Cards[Convert.ToInt64(card[1])];
        var levelFactor = Levels[Convert.ToInt32(card[2])];
        var awakening = Convert.ToInt32(card[5]);
        var storyBonus = Convert.ToInt32(card[8]) switch
        {
            0 => 0, 1 => 2, 2 => 5,
            _ => throw new InvalidOperationException("非法角色剧情阅读阶段")
        };
        var effects = Blooms[master[13].GetInt64()][1].EnumerateArray()
            .Where(row => row[3].GetInt32() <= Convert.ToInt32(card[4]))
            .Select(row => Effects[row[5].GetInt64()]).ToArray();
        // 与客户端一致：先用 float 汇总潜能效果，再截断成整数；星章受该角色稽古高分限制。
        int Effect(int type) => checked((int)effects.Where(row => row[1].GetInt32() == type)
            .Aggregate(0f, (sum, row) => sum + row[4][0][1].GetSingle()));
        var bonus = Bonus(starRank, bestScore);
        var values = Enumerable.Range(0, 3).Select(index => CalculateValue(master[7][index].GetInt32(),
            Effect(index + 1), storyBonus, levelFactor, awakening, Effect(4), bonus)).ToArray();
        return new object?[] { values[0], values[1], values[2], checked(values.Sum()) };
    }

    // 依据 libil2cpp StatusLogic.CalculateCharacterStatus：觉醒和星章加成相加，最后向零截断。
    public static int CalculateValue(int baseValue, int baseStatusUpValue, int storyReadBonus, int levelFactor,
        int awakening, int correctionPercent, float starRankPercent) => checked((int)
        ((levelFactor / 100d * checked(baseValue + baseStatusUpValue + storyReadBonus))
            * ((100d + awakening * 10f + starRankPercent + correctionPercent / 100d) / 100d)));

    static JsonElement[] Read(string resource)
    {
        using var stream = typeof(CharacterStatusRules).Assembly.GetManifestResourceStream(resource + ".json")
            ?? throw new InvalidOperationException($"缺少战力主数据：{resource}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
