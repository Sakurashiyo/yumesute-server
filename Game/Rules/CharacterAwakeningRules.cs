using System.Text.Json;

static class CharacterAwakeningRules
{
    static readonly Dictionary<long, long?> Characters = Read("AdminMaster.CharacterMaster.json").ToDictionary(r => r[0].GetInt64(), r => r[17].ValueKind == JsonValueKind.Null ? (long?)null : r[17].GetInt64());
    static readonly Dictionary<long, Cost[]> Groups = Read("Progression.CharacterAwakeningItemGroupMaster.json").ToDictionary(r => r[0].GetInt64(),
        r => r[1].EnumerateArray().Select(c => new Cost(c[3].GetInt32(), c[4].GetInt64(), c[5].GetInt32())).ToArray());
    public static Cost[] Required(long masterId, int level, int phase)
    {
        if (!Characters.TryGetValue(masterId, out var group)) throw new InvalidOperationException("角色觉醒主数据缺失");
        if (group is null) throw new BadHttpRequestException(CharacterAwakeningErrors.Unsupported, StatusCodes.Status409Conflict);
        if (phase is < 0 or > 1) throw new InvalidOperationException("角色觉醒阶段违反存储约束");
        if (phase == 1) return Array.Empty<Cost>();
        if (level < 25) throw new BadHttpRequestException(CharacterAwakeningErrors.LevelRequired, StatusCodes.Status409Conflict);
        var costs = Groups[group.Value].Where(c => c.Phase == 0).OrderBy(c => c.ItemMasterId).ToArray();
        if (costs.Length == 0) throw new InvalidOperationException("角色觉醒消耗主数据缺失");
        return costs;
    }
    static JsonElement[] Read(string name)
    {
        using var stream = typeof(CharacterAwakeningRules).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"缺少觉醒主数据：{name}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(r => r.Clone()).ToArray();
    }
    public readonly record struct Cost(int Phase, long ItemMasterId, int Quantity);
}
static class CharacterAwakeningErrors
{
    public const string CharacterMissing = "CHARACTER_AWAKENING_CHARACTER_MISSING";
    public const string Unsupported = "CHARACTER_AWAKENING_UNSUPPORTED";
    public const string LevelRequired = "CHARACTER_AWAKENING_LEVEL_REQUIRED";
    public const string ItemInsufficient = "CHARACTER_AWAKENING_ITEM_INSUFFICIENT";
}
