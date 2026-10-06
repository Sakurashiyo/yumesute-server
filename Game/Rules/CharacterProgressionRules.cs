using System.Text.Json;

static class CharacterProgressionRules
{
    public const int LevelCap = 50;
    static readonly Dictionary<long, (int Experience, decimal Bonus)> Items = ReadItems();
    static readonly Dictionary<long, int> Rarities = Read("AdminMaster.CharacterMaster.json").ToDictionary(r => r[0].GetInt64(), r => r[5].GetInt32());
    static readonly int[] Costs = Read("Progression.CharacterLevelMaster.json").OrderBy(r => r[0].GetInt32()).Select(r => r[1].GetInt32()).ToArray();
    public static IEnumerable<long> CharacterMasterIds => Rarities.Keys;
    public static bool TryGetItemExperience(long id, out int experience)
    {
        var found = Items.TryGetValue(id, out var item);
        experience = item.Experience;
        return found;
    }
    public static int Cost(long masterId, int level)
    {
        // 客户端按稀有度乘系数后向零截断，不把单级需求当作累计门槛。
        var scalar = Rarities[masterId] switch { 1 => 0.3d, 2 => 0.5d, 3 => 0.8d, 4 => 1d, _ => throw new InvalidOperationException("未知角色稀有度") };
        return checked((int)(Costs[level - 1] * scalar));
    }
    public static int Minimum(long masterId, int level) => Enumerable.Range(1, level - 1).Sum(l => Cost(masterId, l));
    public static int Maximum(long masterId) => Minimum(masterId, LevelCap) + Cost(masterId, LevelCap);
    public static (int Level, int Experience) ApplyExperience(int level, int experience, int gain, long masterId)
    {
        if (level is < 1 or > LevelCap || experience < 0 || gain <= 0)
            throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        var remaining = (long)experience + gain;
        var next = level;
        while (next < LevelCap && remaining >= Cost(masterId, next)) remaining -= Cost(masterId, next++);
        return (next, (int)Math.Min(remaining, Cost(masterId, next)));
    }
    public static int Remaining(long masterId, int level, int experience) =>
        Enumerable.Range(level, LevelCap - level + 1).Sum(l => Cost(masterId, l)) - experience;
    public static (int Gain, decimal Bonus) UseItems(IEnumerable<(long Id, int Quantity)> items, decimal bonus, int remaining)
    {
        if (bonus < 0) throw new InvalidOperationException("账号经验加成违反存储约束");
        if (remaining <= 0) throw new BadHttpRequestException(CharacterProgressionErrors.LevelCap, StatusCodes.Status409Conflict);
        long gain = 0;
        foreach (var (id, quantity) in items)
        {
            var item = Items[id];
            // 与客户端预览相同：逐张取整，本张使用旧加成，下一张再使用更新后的加成。
            for (var index = 0; index < quantity; index++)
            {
                if (gain >= remaining) throw new BadHttpRequestException(CharacterProgressionErrors.ExcessItems, StatusCodes.Status409Conflict);
                gain = checked(gain + (long)(item.Experience * (100m + bonus) / 100m));
                bonus += item.Bonus;
            }
        }
        return (checked((int)Math.Min(gain, remaining)), bonus);
    }
    static Dictionary<long, (int, decimal)> ReadItems() => Read("Progression.CharacterExperienceItemMaster.json")
        .ToDictionary(r => r[0].GetInt64(), r => (r[2].GetInt32(), (decimal)r[3].GetSingle()));
    static JsonElement[] Read(string name)
    {
        using var stream = typeof(CharacterProgressionRules).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"缺少角色成长主数据：{name}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(r => r.Clone()).ToArray();
    }
}
static class CharacterProgressionErrors
{
    public const string InvalidRequest = "CHARACTER_EXPERIENCE_INVALID_REQUEST";
    public const string CharacterMissing = "CHARACTER_EXPERIENCE_CHARACTER_MISSING";
    public const string ItemInsufficient = "CHARACTER_EXPERIENCE_ITEM_INSUFFICIENT";
    public const string LevelCap = "CHARACTER_EXPERIENCE_LEVEL_CAP";
    public const string ExcessItems = "CHARACTER_EXPERIENCE_EXCESS_ITEMS";
}
