using System.Globalization;
using System.Text.Json;

static class PermanentMarketRules
{
    public sealed record Product(long Id, long ThingId, int Type, int Quantity, int? Limit, DateTimeOffset? Start,
        DateTimeOffset? End, int CostType, long CostId, int Cost);
    public static readonly IReadOnlyDictionary<long, Product> Products = Load();
    static IReadOnlyDictionary<long, Product> Load()
    {
        using var stream = typeof(PermanentMarketRules).Assembly.GetManifestResourceStream("Shop.PermanentMarketThingMaster.json")
            ?? throw new InvalidOperationException("缺少常设商店主数据");
        using var document = JsonDocument.Parse(stream);
        static DateTimeOffset? Date(JsonElement field) => field.ValueKind == JsonValueKind.Null ? null
            : DateTimeOffset.Parse(field.GetProperty("value").GetString()!, CultureInfo.InvariantCulture);
        return document.RootElement.EnumerateArray().ToDictionary(row => row[0].GetInt64(), row =>
        {
            // 当前商品没有解锁条件；新主数据引入条件时必须先实现校验。
            if(row[6].ValueKind != JsonValueKind.Null || row[7].ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException("常设商品解锁条件尚未实现");
            return new Product(row[0].GetInt64(), row[1].GetInt64(), row[2].GetInt32(), row[3].GetInt32(),
                row[4].ValueKind == JsonValueKind.Null ? null : row[4].GetInt32(), Date(row[8]), Date(row[9]),
                row[11].GetInt32(), row[12].GetInt64(), row[13].GetInt32());
        });
    }
}

static class PermanentMarketErrors
{
    public const string InvalidRequest = "SHOP_INVALID_REQUEST";
    public const string Unavailable = "SHOP_PRODUCT_UNAVAILABLE";
    public const string LimitReached = "SHOP_PURCHASE_LIMIT_REACHED";
    public const string Insufficient = "SHOP_BALANCE_INSUFFICIENT";
}
