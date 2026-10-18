using System.Globalization;
using System.Text.Json;

static class JewelShopRules
{
    public sealed record Reward(int Type, long MasterId, int Quantity);
    public sealed record Product(long Id, int Price, int ResetType, int? Limit, int? UnlockType,
        long? UnlockValue, DateTimeOffset Start, DateTimeOffset? End, Reward[] Rewards);

    public static readonly IReadOnlyDictionary<long, Product> Products = Load();

    static IReadOnlyDictionary<long, Product> Load()
    {
        using var stream = typeof(JewelShopRules).Assembly.GetManifestResourceStream("Shop.JewelShopItemMaster.json")
            ?? throw new InvalidOperationException("缺少宝石商店主数据");
        using var document = JsonDocument.Parse(stream);
        var products = new Dictionary<long, Product>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            // Paid 表示消耗有偿宝石；Money 商品需要支付凭证，不能从此接口直接发奖。
            if (row[8].GetInt32() != 1 || row[10].GetBoolean() || row[11].ValueKind != JsonValueKind.Null
                || row[13].ValueKind != JsonValueKind.Null || row[15].ValueKind != JsonValueKind.Null
                || row[16].GetString() != "" || row[18].ValueKind != JsonValueKind.Null
                || row[26].GetInt32() != 0 || row[28].ValueKind != JsonValueKind.Null) continue;
            int? Number(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
            var reset = Number(row[12]) ?? 0;
            var unlock = Number(row[21]);
            if (reset is not (0 or 3) || unlock is not (null or 8 or 16)) continue;
            var rewards = row[25].EnumerateArray().Select(reward =>
                new Reward(reward[3].GetInt32(), reward[2].GetInt64(), reward[4].GetInt32())).ToArray();
            if (rewards.Length == 0 || rewards.Any(reward => reward.Type is not (1 or 12 or 16 or 17)
                || reward.Quantity <= 0 || reward.Type is 16 or 17 && reward.Quantity != 1)) continue;
            DateTimeOffset Date(JsonElement value) =>
                DateTimeOffset.Parse(value.GetProperty("value").GetString()!, CultureInfo.InvariantCulture);
            var price = row[9].GetInt32();
            var limit = Number(row[14]);
            if (price < 0 || limit is <= 0 || unlock is not null && row[22].GetInt64() <= 0)
                throw new InvalidDataException("宝石商品价格、限购或解锁门槛非法");
            var product = new Product(row[0].GetInt64(), price, reset, limit, unlock,
                row[22].ValueKind == JsonValueKind.Null ? null : row[22].GetInt64(),
                Date(row[23]), row[24].ValueKind == JsonValueKind.Null ? null : Date(row[24]), rewards);
            products.Add(product.Id, product);
        }
        return products;
    }

    public static DateTimeOffset NextMonthlyReset(DateTimeOffset now)
    {
        var local = now.ToOffset(TimeSpan.FromHours(9));
        var boundary = new DateTimeOffset(local.Year, local.Month, 1, 5, 0, 0, local.Offset);
        return (local < boundary ? boundary : boundary.AddMonths(1)).ToUniversalTime();
    }
}
