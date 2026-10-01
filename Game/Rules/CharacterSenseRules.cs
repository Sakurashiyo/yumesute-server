using System.Text.Json;

static class CharacterSenseRules
{
    static readonly Dictionary<long, int> CharacterGroups = ReadCharacters();
    static readonly Dictionary<int, SenseCost[]> Groups = ReadGroups();

    public static SenseCost[] Costs(long masterId, int current, int target)
    {
        if (current is < 1 or > 5) throw new InvalidOperationException("角色 Sense 等级违反存储约束");
        if (target is < 1 or > 5) throw new BadHttpRequestException(CharacterSenseErrors.InvalidRequest);
        if (target < current) throw new BadHttpRequestException(CharacterSenseErrors.LevelConflict, StatusCodes.Status409Conflict);
        var group = Groups[CharacterGroups[masterId]];
        for (var level = current; level < target; level++)
            if (!group.Any(cost => cost.Level == level)) throw new InvalidOperationException("缺少 Sense 等级消耗主数据");
        return group.Where(cost => cost.Level >= current && cost.Level < target)
            .GroupBy(cost => cost.ItemMasterId)
            .Select(rows => new SenseCost(current, rows.Key, checked(rows.Sum(cost => cost.Quantity))))
            .OrderBy(cost => cost.ItemMasterId).ToArray();
    }

    static Dictionary<long, int> ReadCharacters()
    {
        using var document = Read("AdminMaster.CharacterMaster.json");
        return document.RootElement.EnumerateArray().ToDictionary(row => row[0].GetInt64(), row => row[14].GetInt32());
    }

    static Dictionary<int, SenseCost[]> ReadGroups()
    {
        using var document = Read("Progression.CharacterSenseEnhanceItemGroupMaster.json");
        return document.RootElement.EnumerateArray().ToDictionary(row => row[0].GetInt32(),
            row => row[1].EnumerateArray().Select(cost => new SenseCost(cost[0].GetInt32(), cost[1].GetInt64(), cost[2].GetInt32())).ToArray());
    }

    static JsonDocument Read(string name)
    {
        using var stream = typeof(CharacterSenseRules).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"缺少 Sense 成长主数据：{name}");
        return JsonDocument.Parse(stream);
    }

    public readonly record struct SenseCost(int Level, long ItemMasterId, int Quantity);
}

static class CharacterSenseErrors
{
    public const string InvalidRequest = "CHARACTER_SENSE_INVALID_REQUEST";
    public const string LevelConflict = "CHARACTER_SENSE_LEVEL_CONFLICT";
    public const string ItemInsufficient = "CHARACTER_SENSE_ITEM_INSUFFICIENT";
    public const string CharacterMissing = "CHARACTER_SENSE_CHARACTER_MISSING";
}
