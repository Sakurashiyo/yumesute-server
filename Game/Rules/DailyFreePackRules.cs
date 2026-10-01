using System.Text.Json;

static class DailyFreePackRules
{
    public const long ProductId = 4101;
    public sealed record Reward(int Type, int Quantity);
    public static readonly int PurchaseLimit;
    public static readonly Reward[] Rewards;

    static DailyFreePackRules()
    {
        using var stream = typeof(DailyFreePackRules).Assembly.GetManifestResourceStream("Shop.JewelShopItemMaster.json")
            ?? throw new InvalidOperationException("缺少宝石商店主数据");
        using var document = JsonDocument.Parse(stream);
        var row = document.RootElement.EnumerateArray().Single(value => value[0].GetInt64() == ProductId);
        // 这里只实现无需支付的每日包，主数据变更不能静默绕过付费或解锁条件。
        if (row[8].GetInt32() != 3 || row[12].GetInt32() != 1 || row[14].GetInt32() != 1
            || row[20].GetBoolean() || row[21].ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("每日免费包的购买规则发生变化");
        PurchaseLimit = row[14].GetInt32();
        Rewards = row[25].EnumerateArray().Select(thing =>
        {
            var type = thing[3].GetInt32();
            var quantity = thing[4].GetInt32();
            if (type is not (12 or 15) || quantity <= 0)
                throw new InvalidOperationException("每日免费包使用尚未实现的奖励");
            return new Reward(type, quantity);
        }).ToArray();
    }
}
