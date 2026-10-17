using System.Text.Json;

static class HistoricalGachaResources
{
    public static readonly long[] Ids = Load();
    static long[] Load()
    {
        using var stream = typeof(HistoricalGachaResources).Assembly.GetManifestResourceStream("AdminWeb.historical-gachas.json")
            ?? throw new InvalidOperationException("缺少已核验往期卡池目录");
        return JsonSerializer.Deserialize<long[]>(stream) ?? throw new InvalidDataException("往期卡池目录为空");
    }
    public static bool Available(GachaRules.Pool pool, string resourcesRoot)
    {
        if (!pool.Historical) return true;
        var paths = new List<string> {
            $"gachas_assets_gachas/background_{pool.Id}.bundle",
            $"gachas_assets_gachas/logo_{pool.Id}.bundle",
            $"shopexchangetab_assets_shopexchangetab/{pool.ExchangeBanner}.bundle",
            $"Textures/Banners/Gacha/{pool.Id}.astc.gz"
        };
        foreach (var entry in pool.Entries)
        {
            if (entry.Type == 3) paths.Add($"posters_assets_posters/{entry.MasterId}.bundle");
            else
            {
                var card = Cards.Value[entry.MasterId];
                var asset = card[4] is string named ? named : entry.MasterId.ToString();
                for (var phase = 0; phase < (entry.Rarity >= 4 ? 2 : 1); phase++)
                    paths.Add($"charactercards_assets_charactercard/{asset}_{phase}.bundle");
            }
        }
        return paths.All(path => File.Exists(Path.Combine(resourcesRoot,path)));
    }
    static readonly Lazy<Dictionary<long,object?[]>> Cards = new(() =>
        GachaRules.ReadRows("AdminMaster.CharacterMaster.json").Cast<object?[]>().ToDictionary(row => GachaRules.Long(row[0])));
}
