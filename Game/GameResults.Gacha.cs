using System.Text.Json;

static partial class GameResults
{
    public static object?[] GachaListResult()
    {
        return new object?[]
        {
            new object?[] { 1167, new object?[] { new object?[] { 116700, 1 }, new object?[] { 116706, 1 } }, 7, 6 },
            new object?[] { 1641, new object?[] { new object?[] { 164100, 1 }, new object?[] { 164106, 1 } }, 7, 6 },
            new object?[] { 2155, new object?[] { new object?[] { 215500, 1 }, new object?[] { 215506, 1 } }, 7, 6 },
            new object?[] { 2519, new object?[] { new object?[] { 251900, 1 }, new object?[] { 251906, 1 } }, 7, 6 },
            new object?[] { 3027, new object?[] { new object?[] { 302700, 1 }, new object?[] { 302706, 1 } }, 7, 6 },
            new object?[] { 5508, Array.Empty<object?>(), 7, 6 }
        };
    }

    public static object?[] GachaRollResult(long gachaDetailMasterId)
    {
        var rewards = RollGachaRewards(gachaDetailMasterId);

        return new object?[]
        {
            gachaDetailMasterId,
            rewards,
            Array.Empty<object?>(),
            Array.Empty<object?>(),
            Array.Empty<object?>()
        };
    }

    public static object?[] ReRollGachaResult(long? gachaDetailMasterId)
    {
        return GachaRollResult(gachaDetailMasterId ?? 193300);
    }

    public static object?[] CharacterLineupResult(long gachaMasterId)
    {
        var catalog = GachaCatalog.Value;
        if (!catalog.Pools.TryGetValue(gachaMasterId, out var pool) || pool.ActorEntries.Count == 0)
        {
            return new object?[]
            {
                new object?[] { new object?[] { 2, 0.82 }, new object?[] { 3, 0.15 }, new object?[] { 4, 0.03 } },
                new object?[] { new object?[] { 2, 0.0 }, new object?[] { 3, 0.97 }, new object?[] { 4, 0.03 } },
                Array.Empty<object?>()
            };
        }

        return new object?[]
        {
            BuildRarityRateRows(pool.NormalRates),
            BuildRarityRateRows(pool.GuaranteedRates),
            BuildCharacterRateRows(pool)
        };
    }

    public static object?[] GachaHistoryResult()
    {
        lock (GachaHistorySync)
        {
            return GachaHistories
                .OrderByDescending(history => history.RolledAt)
                .Take(100)
                .Select(history => new object?[] { history.MasterId, history.RolledAt })
                .ToArray();
        }
    }

    static object?[] GachaReward(object?[] receivedThing)
    {
        return new object?[]
        {
            new object?[] { receivedThing },
            Array.Empty<object?>(),
            null
        };
    }

    static object?[] RollGachaRewards(long gachaDetailMasterId)
    {
        var catalog = GachaCatalog.Value;
        var detail = catalog.Details.TryGetValue(gachaDetailMasterId, out var knownDetail)
            ? knownDetail
            : catalog.Details.TryGetValue(gachaDetailMasterId / 100, out var fallbackDetail)
                ? fallbackDetail
                : null;
        var pool = detail is not null && catalog.Pools.TryGetValue(detail.PoolId, out var knownPool)
            ? knownPool
            : catalog.Pools.Values.FirstOrDefault(pool => pool.ActorEntries.Count > 0);

        if (pool is null)
        {
            return DefaultActorRewards();
        }

        var drawCount = detail?.DrawCount ?? (gachaDetailMasterId % 10 == 4 || gachaDetailMasterId >= 500000 ? 10 : 1);
        var rewards = new List<object?[]>();
        for (var i = 0; i < Math.Max(drawCount, 1); i++)
        {
            var isTenthDraw = drawCount >= 10 && i == 9;
            var entry = pool.ActorEntries.Count > 0
                ? RollActorEntry(pool, isTenthDraw)
                : RollAnyEntry(pool);
            rewards.Add(GachaReward(entry.ToReceivedThing()));
            RecordGachaHistory(entry);
        }

        return rewards.ToArray();
    }

    static GachaEntry RollActorEntry(GachaPool pool, bool isTenthDraw)
    {
        var rates = isTenthDraw ? pool.GuaranteedRates : pool.NormalRates;
        var rarity = RollRarity(rates);
        var entries = pool.ActorEntries.Where(entry => entry.Rarity == rarity).ToArray();
        if (entries.Length == 0)
        {
            entries = pool.ActorEntries.ToArray();
        }

        if (rarity != 4)
        {
            return entries[Random.Shared.Next(entries.Length)];
        }

        var weightedEntries = BuildWeightedEntriesForRarity(pool, rarity);
        return weightedEntries.Length == 0
            ? entries[Random.Shared.Next(entries.Length)]
            : PickWeighted(weightedEntries);
    }

    static GachaEntry RollAnyEntry(GachaPool pool)
    {
        var entries = pool.AllEntries.Count > 0 ? pool.AllEntries : DefaultPosterEntries();
        return entries[Random.Shared.Next(entries.Count)];
    }

    static int RollRarity(IReadOnlyDictionary<int, double> rates)
    {
        var total = rates.Values.Sum();
        var roll = Random.Shared.NextDouble() * total;
        foreach (var rate in rates.OrderBy(rate => rate.Key))
        {
            roll -= rate.Value;
            if (roll <= 0) return rate.Key;
        }

        return rates.Keys.Max();
    }

    static GachaEntry PickWeighted(IReadOnlyList<(GachaEntry Entry, double Weight)> entries)
    {
        var total = entries.Sum(entry => entry.Weight);
        var roll = Random.Shared.NextDouble() * total;
        foreach (var entry in entries)
        {
            roll -= entry.Weight;
            if (roll <= 0) return entry.Entry;
        }

        return entries[^1].Entry;
    }

    static (GachaEntry Entry, double Weight)[] BuildWeightedEntriesForRarity(GachaPool pool, int rarity)
    {
        var entries = pool.ActorEntries.Where(entry => entry.Rarity == rarity).ToArray();
        if (entries.Length == 0) return Array.Empty<(GachaEntry Entry, double Weight)>();

        var categoryRate = pool.NormalRates.TryGetValue(rarity, out var rate) ? rate : 0.0;
        var pickupEntries = entries.Where(entry => entry.IsPickup).ToArray();
        if (rarity != 4 || pickupEntries.Length == 0)
        {
            var eachRate = categoryRate / entries.Length;
            return entries.Select(entry => (entry, eachRate)).ToArray();
        }

        var pickupEachRate = 0.00750;
        var pickupTotalRate = Math.Min(categoryRate, pickupEntries.Length * pickupEachRate);
        var normalEntries = entries.Where(entry => !entry.IsPickup).ToArray();
        var normalEachRate = normalEntries.Length == 0 ? 0.0 : Math.Max(0.0, categoryRate - pickupTotalRate) / normalEntries.Length;
        return entries
            .Select(entry => (Entry: entry, Weight: entry.IsPickup ? pickupEachRate : normalEachRate))
            .Where(entry => entry.Weight > 0)
            .ToArray();
    }

    static object?[] BuildRarityRateRows(IReadOnlyDictionary<int, double> rates)
    {
        return rates.OrderBy(rate => rate.Key)
            .Select(rate => new object?[] { rate.Key, Math.Round(rate.Value, 7) })
            .ToArray();
    }

    static object?[] BuildCharacterRateRows(GachaPool pool)
    {
        return pool.ActorEntries
            .OrderBy(entry => entry.GachaEntryId)
            .Select(entry =>
            {
                var weight = BuildWeightedEntriesForRarity(pool, entry.Rarity)
                    .FirstOrDefault(weighted => weighted.Entry.GachaEntryId == entry.GachaEntryId)
                    .Weight;
                return new object?[] { entry.GachaEntryId, null, Math.Round(weight, 7) };
            })
            .ToArray();
    }

    static void RecordGachaHistory(GachaEntry entry)
    {
        if (entry.ThingType != 2) return;
        lock (GachaHistorySync)
        {
            GachaHistories.Add(new GachaHistory(entry.MasterId, DateTime.UtcNow));
            if (GachaHistories.Count > 200)
            {
                GachaHistories.RemoveRange(0, GachaHistories.Count - 200);
            }
        }
    }

    static object?[] DefaultActorRewards()
    {
        return new object?[]
        {
            GachaReward(new object?[] { 2, 120100, 1, null, null, null, false }),
            GachaReward(new object?[] { 2, 140150, 1, null, null, null, false }),
            GachaReward(new object?[] { 2, 140790, 1, null, null, null, false }),
            GachaReward(new object?[] { 2, 120200, 1, null, null, null, false }),
            GachaReward(new object?[] { 2, 120110, 1, null, null, null, false }),
            GachaReward(new object?[] { 2, 130030, 1, null, null, null, false }),
            GachaReward(new object?[] { 2, 120030, 1, null, null, null, false }),
            GachaReward(new object?[] { 2, 130130, 1, null, null, null, false }),
            GachaReward(new object?[] { 2, 140000, 1, null, null, null, false }),
            GachaReward(new object?[] { 2, 120190, 1, null, null, null, false })
        };
    }

    static List<GachaEntry> DefaultPosterEntries()
    {
        return new List<GachaEntry>
        {
            new(220090, 3, 220090, 0, false),
            new(210010, 3, 210010, 0, false),
            new(230940, 3, 230940, 0, false),
            new(210180, 3, 210180, 0, false)
        };
    }

    static readonly object GachaHistorySync = new();
    static readonly List<GachaHistory> GachaHistories = new();

    static Lazy<GachaCatalogData> GachaCatalog { get; } = new(LoadGachaCatalog);

    static GachaCatalogData LoadGachaCatalog()
    {
        var characterRarities = LoadCharacterRarities();
        var pools = new Dictionary<long, GachaPool>();
        var details = new Dictionary<long, GachaDetail>();
        var path = FindMasterDataFile("GachaMaster.json");
        if (path is null)
        {
            return new GachaCatalogData(pools, details);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 10) continue;
            var poolId = row[0].GetInt64();
            var pool = new GachaPool(poolId, IsYumeFesRatePool(row));

            foreach (var detail in row[8].EnumerateArray())
            {
                if (detail.ValueKind != JsonValueKind.Array || detail.GetArrayLength() < 9) continue;
                var detailId = detail[0].GetInt64();
                var drawCount = detail[8].GetInt32();
                details[detailId] = new GachaDetail(poolId, drawCount);
            }

            details.TryAdd(poolId, new GachaDetail(poolId, 1));

            foreach (var entry in row[9].EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 5) continue;
                var thingType = entry[2].GetInt32();
                var masterId = entry[1].GetInt64();
                var rarity = thingType == 2 && characterRarities.TryGetValue(masterId, out var characterRarity)
                    ? characterRarity
                    : 0;
                var isPickup = entry[4].ValueKind != JsonValueKind.Null;
                var gachaEntry = new GachaEntry(entry[0].GetInt64(), thingType, masterId, rarity, isPickup);
                pool.AllEntries.Add(gachaEntry);
                if (thingType == 2 && rarity is >= 2 and <= 4)
                {
                    pool.ActorEntries.Add(gachaEntry);
                }
            }

            pools[poolId] = pool;
        }

        return new GachaCatalogData(pools, details);
    }

    static bool IsYumeFesRatePool(JsonElement row)
    {
        if (row.GetArrayLength() <= 20) return false;
        return row[2].ValueKind == JsonValueKind.Number
            && row[2].GetInt32() == 1
            && row[20].ValueKind == JsonValueKind.Number
            && row[20].GetInt32() == 1;
    }

    static Dictionary<long, int> LoadCharacterRarities()
    {
        var result = new Dictionary<long, int>();
        var path = FindMasterDataFile("CharacterMaster.json");
        if (path is null) return result;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() <= 5) continue;
            result[row[0].GetInt64()] = row[5].GetInt32();
        }

        return result;
    }

    static string? FindMasterDataFile(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "masterdata-export", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "masterdata-export", fileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    sealed record GachaHistory(long MasterId, DateTime RolledAt);

    sealed record GachaCatalogData(Dictionary<long, GachaPool> Pools, Dictionary<long, GachaDetail> Details);

    sealed record GachaDetail(long PoolId, int DrawCount);

    sealed class GachaPool
    {
        public GachaPool(long poolId, bool isYumeFesRatePool)
        {
            PoolId = poolId;
            NormalRates = isYumeFesRatePool
                ? new Dictionary<int, double> { [2] = 0.79, [3] = 0.15, [4] = 0.06 }
                : new Dictionary<int, double> { [2] = 0.82, [3] = 0.15, [4] = 0.03 };
            GuaranteedRates = isYumeFesRatePool
                ? new Dictionary<int, double> { [2] = 0.0, [3] = 0.94, [4] = 0.06 }
                : new Dictionary<int, double> { [2] = 0.0, [3] = 0.97, [4] = 0.03 };
        }

        public long PoolId { get; }
        public IReadOnlyDictionary<int, double> NormalRates { get; }
        public IReadOnlyDictionary<int, double> GuaranteedRates { get; }
        public List<GachaEntry> AllEntries { get; } = new();
        public List<GachaEntry> ActorEntries { get; } = new();
    }

    sealed record GachaEntry(long GachaEntryId, int ThingType, long MasterId, int Rarity, bool IsPickup)
    {
        public object?[] ToReceivedThing()
        {
            return ThingType == 3
                ? new object?[] { ThingType, MasterId, 1, null, null, 0, false }
                : new object?[] { ThingType, MasterId, 1, null, null, null, false };
        }
    }
}
