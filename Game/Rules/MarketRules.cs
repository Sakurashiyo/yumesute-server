using System.Text.Json;

static class MarketRules
{
    public const int SlotCount = 15;
    public const int RefreshCost = 20;
    public const int RefreshLimit = 10;
    public sealed record Product(long Id, long ThingId, int ThingType, int ThingQuantity, int RequiredType, long RequiredId, int RequiredQuantity);
    public static readonly IReadOnlyDictionary<int, Product[]> ProductsBySlot = Load();
    static IReadOnlyDictionary<int, Product[]> Load()
    {
        using var stream = typeof(MarketRules).Assembly.GetManifestResourceStream("Shop.MarketFrameThingMaster.json") ?? throw new InvalidOperationException("缺少随机商店主数据");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(row => new Product(row[0].GetInt64(), row[1].GetInt64(), row[2].GetInt32(), row[3].GetInt32(), row[6].GetInt32(), row[7].GetInt64(), row[8].GetInt32())).GroupBy(product => (int)(product.Id / 100)).ToDictionary(group => group.Key, group => group.ToArray());
    }
}
static class MarketErrors
{
    public const string InvalidRequest = "SHOP_MARKET_INVALID_REQUEST";
    public const string Unavailable = "SHOP_MARKET_UNAVAILABLE";
    public const string Insufficient = "SHOP_BALANCE_INSUFFICIENT";
    public const string RefreshLimit = "SHOP_MARKET_REFRESH_LIMIT_REACHED";
}
