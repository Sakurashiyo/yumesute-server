using System.Text.Json;

static class CharacterProgressionRules
{
    const int LevelCap = 50;

    static readonly IReadOnlyDictionary<long, int> ExperienceItems = ReadExperienceItems();
    static readonly int[] ExperienceThresholds = ReadExperienceThresholds();

    public static bool TryGetItemExperience(long itemMasterId, out int experience) =>
        ExperienceItems.TryGetValue(itemMasterId, out experience);

    public static (int Level, int Experience) ApplyExperience(int level, int experience, int gain)
    {
        if (level is < 1 or > LevelCap || experience < 0 || gain <= 0)
        {
            throw new BadHttpRequestException(CharacterProgressionErrors.InvalidRequest);
        }

        var totalExperience = checked(experience + gain);
        var nextLevel = 1;
        foreach (var threshold in ExperienceThresholds)
        {
            if (threshold > totalExperience || nextLevel >= LevelCap) break;
            nextLevel++;
        }

        var cappedExperience = nextLevel == LevelCap
            ? Math.Min(totalExperience, ExperienceThresholds[LevelCap - 2])
            : totalExperience;
        return (Math.Max(level, nextLevel), cappedExperience);
    }

    static IReadOnlyDictionary<long, int> ReadExperienceItems()
    {
        using var document = ReadTable("CharacterExperienceItemMaster");
        return document.RootElement.EnumerateArray().ToDictionary(
            row => row[0].GetInt64(),
            row => row[2].GetInt32());
    }

    static int[] ReadExperienceThresholds()
    {
        using var document = ReadTable("CharacterLevelMaster");
        return document.RootElement.EnumerateArray()
            .Where(row => row[0].GetInt32() < LevelCap)
            .OrderBy(row => row[0].GetInt32())
            .Select(row => row[1].GetInt32())
            .ToArray();
    }

    static JsonDocument ReadTable(string name)
    {
        using var stream = typeof(CharacterProgressionRules).Assembly
            .GetManifestResourceStream($"Progression.{name}.json")
            ?? throw new InvalidOperationException($"缺少角色成长主数据：{name}");
        return JsonDocument.Parse(stream);
    }
}

static class CharacterProgressionErrors
{
    public const string InvalidRequest = "CHARACTER_EXPERIENCE_INVALID_REQUEST";
    public const string CharacterMissing = "CHARACTER_EXPERIENCE_CHARACTER_MISSING";
    public const string ItemInsufficient = "CHARACTER_EXPERIENCE_ITEM_INSUFFICIENT";
}
