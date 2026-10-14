using System.Text.Json;

static class CircleSupportRules
{
    internal sealed record Material(int Order,long ItemId,long Quantity);
    internal sealed record Limit(int Level,long Coin,DateTime Release,Material[] Materials);
    static JsonDocument Read(string name)
    {
        using var stream=typeof(CircleSupportRules).Assembly.GetManifestResourceStream("Circle."+name+".json")
            ?? throw new InvalidOperationException("缺少社团主数据："+name);
        return JsonDocument.Parse(stream);
    }
    static readonly Lazy<Limit[]> limits=new(() =>
    {
        using var document=Read("CircleSupportCompanyLevelLimitMaster");
        return document.RootElement.EnumerateArray().Select(row => new Limit(row[1].GetInt32(),row[2].GetInt64(),
            DateTime.Parse(row[3].GetProperty("value").GetString()!,null,System.Globalization.DateTimeStyles.AdjustToUniversal),
            row[4].EnumerateArray().Select(item=>new Material(item[0].GetInt32(),item[1].GetInt64(),item[2].GetInt64())).ToArray())).ToArray();
    });
    public static Limit? NextLimit(int current,DateTime now) => limits.Value.Where(row=>row.Level>current && row.Release<=now)
        .OrderBy(row=>row.Level).FirstOrDefault();
    static readonly Lazy<Dictionary<int,int>> theaterStamina=new(() =>
    {
        using var document=Read("CircleTheaterLevelMaster");
        return document.RootElement.EnumerateArray().ToDictionary(row=>row[1].GetInt32(),row=>row[2].GetInt32());
    });
    // 客户端 GetCurrentTheaterLevel 直接相加四个剧团等级，体力产量来自同等级主数据。
    public static int TheaterDailyStamina(IEnumerable<object?> support) =>
        theaterStamina.Value[support.Cast<object?[]>().Sum(row=>Convert.ToInt32(row[0]))];
    public static int InitialLimit => limits.Value.Min(row=>row.Level)-1;
}
