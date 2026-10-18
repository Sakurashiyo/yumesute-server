using System.Globalization;
using System.Text.Json;

static class RatingShopRules
{
    public sealed record Product(long Id, int RequiredRating, long NameColorId, int Coins,
        DateTimeOffset Start, DateTimeOffset? End);

    public static readonly IReadOnlyDictionary<long, Product> Products = Load();

    static IReadOnlyDictionary<long, Product> Load()
    {
        using var stream = typeof(RatingShopRules).Assembly.GetManifestResourceStream("Shop.JewelShopItemMaster.json")
            ?? throw new InvalidOperationException("缺少宝石商店主数据");
        using var document = JsonDocument.Parse(stream);
        var products = new Dictionary<long, Product>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row[21].ValueKind != JsonValueKind.Number || row[21].GetInt32() != 8
                || !row[25].EnumerateArray().Any(reward => reward[3].GetInt32() == 9)) continue;
            // 仅接入免费且每账号一次的 Rating 名称颜色包，不能绕过其他商品的支付条件。
            if (row[8].GetInt32() != 3 || row[9].GetInt32() != 0 || row[12].GetInt32() != 0
                || row[14].GetInt32() != 1 || !row[20].GetBoolean())
                throw new InvalidOperationException("Rating 名称颜色包的免费领取规则发生变化");
            var rewards = row[25].EnumerateArray().ToArray();
            if (rewards.Length != 2 || rewards.Any(reward => reward[3].GetInt32() is not (9 or 12))
                || rewards.Count(reward => reward[3].GetInt32() == 9) != 1
                || rewards.Single(reward => reward[3].GetInt32() == 9)[4].GetInt32() != 1)
                throw new InvalidOperationException("Rating 名称颜色包的奖励规则发生变化");
            var color = rewards.Single(reward => reward[3].GetInt32() == 9)[2].GetInt64();
            var coins = rewards.Single(reward => reward[3].GetInt32() == 12)[4].GetInt32();
            if (row[22].GetInt32() <= 0 || color <= 0 || coins <= 0)
                throw new InvalidOperationException("Rating 名称颜色包的门槛或奖励非法");
            DateTimeOffset Date(JsonElement value) =>
                DateTimeOffset.Parse(value.GetProperty("value").GetString()!, CultureInfo.InvariantCulture);
            var product = new Product(row[0].GetInt64(), row[22].GetInt32(), color, coins, Date(row[23]),
                row[24].ValueKind == JsonValueKind.Null ? null : Date(row[24]));
            products.Add(product.Id, product);
        }
        return products;
    }
}
