using System.Text.Json;

sealed class AdminSaveCatalog
{
    public sealed record Entry(long Id, string Name, long BaseId = 0, int Rarity = 0, int MaxQuantity = 0, string ImageKey = "");
    public sealed record BaseInfo(long Id, string Name, long DefaultCostume);
    public IReadOnlyDictionary<long, Entry> Cards { get; }
    public IReadOnlyDictionary<long, Entry> Costumes { get; }
    public IReadOnlyDictionary<long, Entry> Items { get; }
    public IReadOnlyDictionary<long, BaseInfo> Bases { get; }
    public IReadOnlyDictionary<long, IReadOnlySet<long>> CostumeWearableBaseIds { get; }

    public bool CanWearCostume(long costumeId, long characterBaseMasterId) =>
        CostumeWearableBaseIds.TryGetValue(costumeId, out var bases)
        && bases.Contains(characterBaseMasterId);

    public AdminSaveCatalog()
    {
        Bases = Read("CharacterBaseMaster").ToDictionary(row => row[0].GetInt64(),
            row => new BaseInfo(row[0].GetInt64(), row[1].GetString()!, row[24].GetInt64()));
        Cards = Read("CharacterMaster").ToDictionary(row => row[0].GetInt64(), row =>
        {
            var baseId = row[1].GetInt64();
            return new Entry(row[0].GetInt64(), $"{Bases[baseId].Name} · {row[2].GetString()}", baseId, row[5].GetInt32(),
                ImageKey: $"{row[4].GetString() ?? row[0].GetInt64().ToString()}_0");
        });
        var groups = Read("CostumeGroupMaster").ToDictionary(row => row[0].GetInt64(), row => row[5].GetInt64());
        var wearableGroups = Read("CostumeWearableCharacterGroupMaster").ToDictionary(
            row => row[0].GetInt64(),
            row => (IReadOnlySet<long>)row[1].EnumerateArray().Select(value => value.GetInt64()).ToHashSet());
        CostumeWearableBaseIds = Read("CostumeMaster").ToDictionary(
            row => row[0].GetInt64(),
            row => wearableGroups[groups[row[4].GetInt64()]]);
        Costumes = Read("CostumeMaster").ToDictionary(row => row[0].GetInt64(), row =>
        {
            var group = groups[row[4].GetInt64()];
            var wearableBases = wearableGroups[group];
            var owner = wearableBases.Count == 1 && Bases.TryGetValue(wearableBases.Single(), out var character)
                ? character.Name
                : $"服装组 {group}";
            return new Entry(row[0].GetInt64(), $"{owner} · {row[1].GetString()}",
                BaseId: wearableBases.Count == 1 ? wearableBases.Single() : 0,
                ImageKey: row[0].GetInt64().ToString());
        });
        Items = Read("ItemMaster").ToDictionary(row => row[0].GetInt64(),
            row => new Entry(row[0].GetInt64(), row[1].GetString()!, MaxQuantity: row[5].GetInt32(), ImageKey: row[0].GetInt64().ToString()));
    }

    static JsonElement[] Read(string table)
    {
        using var stream = typeof(AdminSaveCatalog).Assembly.GetManifestResourceStream($"AdminMaster.{table}.json")
            ?? throw new InvalidOperationException($"缺少存档管理主数据：{table}");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
