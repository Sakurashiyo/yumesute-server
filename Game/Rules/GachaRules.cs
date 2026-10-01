using System.Security.Cryptography;
using System.Text.Json;

static class GachaRules
{
    public const long DuplicateItemId = 140000;
    public const int ExchangeCost = 200;
    public sealed record Entry(long Id, int Type, long MasterId, int Rarity, int MaxPhase, long[] Costumes);
    public sealed record Pool(long Id, int Type, long PointId, Entry[] Entries)
    {
        public int TopRarity => Type == 2 ? 4 : 3;
        public string AssetName => Id.ToString();
        public long TemplateId => Type == 2 ? 1167 : 2155;
        public string ExchangeBanner => Type == 2 ? "1874" : "2860";
        public long TextTemplateId => Type == 2 ? 1860 : 2860;
        public double[] Rates => Type == 2 ? [0.82,0.15,0.03] : [0.80,0.16,0.04];
        public long ExchangeId(Entry entry) => Id * 1000000 + entry.MasterId;
    }
    public sealed record Detail(Pool Pool, int Count) { public int Cost => Count * 300; }
    public static readonly object?[] SourceGachas = ReadRows("Gacha.GachaMaster.json");
    static readonly Dictionary<long, object?[]> Characters = ReadRows("AdminMaster.CharacterMaster.json").Cast<object?[]>().ToDictionary(row=>Long(row[0]));
    static readonly Dictionary<long, object?[]> Posters = ReadRows("Shop.PosterMaster.json").Cast<object?[]>().ToDictionary(row=>Long(row[0]));
    public static readonly Pool[] Pools = [BuildPool(1888,2,411167),BuildPool(2860,3,412155)];
    public static readonly IReadOnlyDictionary<long, Detail> Details = Pools.SelectMany(pool=>new[] {
        KeyValuePair.Create(pool.Id*100+1,new Detail(pool,1)),KeyValuePair.Create(pool.Id*100+2,new Detail(pool,10))
    }).ToDictionary(pair=>pair.Key,pair=>pair.Value);
    public static readonly IReadOnlyDictionary<long, (Pool Pool,Entry Entry)> Exchanges = Pools
        .SelectMany(pool=>pool.Entries.Where(entry=>entry.Rarity==pool.TopRarity)
        .Select(entry=>KeyValuePair.Create(pool.ExchangeId(entry),(pool,entry)))).ToDictionary(pair=>pair.Key,pair=>pair.Value);

    static Pool BuildPool(long id,int type,long pointId)
    {
        var ids = SourceGachas.Cast<object?[]>().SelectMany(row=>((object?[])row[9]!).Cast<object?[]>())
            .Where(row=>Long(row[2])==type).Select(row=>Long(row[1])).Distinct().Order().ToArray();
        var entries = ids.Select(masterId=> {
            var row = (type==2?Characters:Posters)[masterId];
            var rarity = checked((int)Long(row[type==2?5:3]));
            var costumes = type==2 ? ((object?[])row[21]!).Cast<object?[]>().Where(c=>c[1] is false).Select(c=>Long(c[0])).ToArray() : Array.Empty<long>();
            var maxPhase = type==3 ? checked((int)Long(row[25])) : 0;
            return new Entry(id*1000000+masterId,type,masterId,rarity,maxPhase,costumes);
        }).ToArray();
        var first = type==2?2:1;
        if(entries.Length==0 || entries.Any(e=>e.Rarity<first || e.Rarity>first+2)
            || Enumerable.Range(first,3).Any(r=>!entries.Any(e=>e.Rarity==r)))
            throw new InvalidDataException("统一奖池缺少稀有度或包含非法主数据");
        return new(id,type,pointId,entries);
    }

    // 第十抽使用独立保证档，最高稀有度概率不变，其余概率合并到次高档。
    public static Entry Draw(Pool pool,bool guaranteed)
    {
        var roll = RandomNumberGenerator.GetInt32(10000);
        var rarity=Select(pool,guaranteed,roll,0).Rarity;
        var candidates=pool.Entries.Where(entry=>entry.Rarity==rarity).ToArray();
        return candidates[RandomNumberGenerator.GetInt32(candidates.Length)];
    }
    internal static Entry Select(Pool pool,bool guaranteed,int roll,int index)
    {
        if(roll is <0 or >=10000 || index<0) throw new ArgumentOutOfRangeException(nameof(roll));
        var top = pool.Type==2?300:400;
        var middle = pool.Type==2?1500:1600;
        var rarity = roll<top?pool.TopRarity:guaranteed || roll<top+middle?pool.TopRarity-1:pool.TopRarity-2;
        var candidates = pool.Entries.Where(entry=>entry.Rarity==rarity).ToArray();
        return candidates[index%candidates.Length];
    }
    public static int DuplicateQuantity(Entry entry) => (entry.Rarity-(entry.Type==2?2:1)) switch {0=>1,1=>10,2=>100,_=>throw new InvalidOperationException("重复奖励稀有度非法")};
    public static object?[] Received(int type,long masterId,int quantity,int? originalType=null,long? originalId=null,int? phase=null)
        => [type,masterId,quantity,originalType,originalId,phase,false];
    public static object?[] Lineup(long poolId,int type)
    {
        var pool = Pools.SingleOrDefault(p=>p.Id==poolId && p.Type==type)
            ?? throw new BadHttpRequestException(GachaErrors.Unavailable,404);
        object?[] Rates(bool fixedDraw) => Enumerable.Range(pool.TopRarity-2,3)
            .Select((r,i)=>(object?)new object?[] {r,fixedDraw?(i==0?0d:i==1?1-pool.Rates[2]:pool.Rates[2]):pool.Rates[i]}).ToArray();
        object?[] Items(bool fixedDraw) => pool.Entries.Select(e=>(object?)new object?[] {e.Id,null,
            (fixedDraw?(e.Rarity==pool.TopRarity?pool.Rates[2]:e.Rarity==pool.TopRarity-1?1-pool.Rates[2]:0d):pool.Rates[e.Rarity-(pool.TopRarity-2)])
            /pool.Entries.Count(other=>other.Rarity==e.Rarity)}).ToArray();
        return [Rates(false),Rates(true),Items(false),Items(true)];
    }
    public static object?[] MasterGachas(object?[] originals) => Pools.Select(pool=> {
        var row=(object?[])originals.Cast<object?[]>().Single(r=>Long(r[0])==pool.TemplateId).Clone();
        row[0]=pool.Id;row[1]=pool.Type==2?"全アクターガチャ":"全ポスターガチャ";
        row[3]=8;row[4]=pool.TextTemplateId;row[5]=Start;row[6]=End;
        row[7]=new object?[] {new object?[] {pool.Id*100+1,pool.PointId,1,1}};
        row[8]=((object?[])row[8]!).Cast<object?[]>().Where(d=>Long(d[0])==pool.TemplateId*100+1 || Long(d[0])==pool.TemplateId*100+2)
            .Select(d=> { var copy=(object?[])d.Clone();var detailId=pool.Id*100+Long(d[0])%100;copy[0]=detailId;copy[1]=null;copy[2]=null;copy[3]=Details[detailId].Cost;copy[4]=null;copy[5]=false;copy[6]=null;copy[7]=null;copy[8]=Details[detailId].Count;copy[11]=Array.Empty<object?>();return copy; }).ToArray();
        row[9]=pool.Entries.Select(e=>new object?[] {e.Id,e.MasterId,e.Type,1,null,false}).ToArray();
        row[10]=false;row[11]=null;row[13]=true;row[14]=true;row[15]=0;row[16]=null;row[17]=pool.ExchangeBanner;row[18]=pool.Type;row[19]=0;row[20]=pool.Type;row[21]=Array.Empty<object?>();row[22]=null;row[23]=null;
        return (object?)row;
    }).ToArray();
    public static object?[] MasterShops(object?[] originals)
    {
        var others=originals.Cast<object?[]>().Where(r=>Long(r[2])!=4).Cast<object?>();
        return others.Concat(Pools.Select(pool=> {
            var row=(object?[])originals.Cast<object?[]>().Single(r=>Long(r[0])==pool.TemplateId && Long(r[2])==4).Clone();
            row[0]=pool.Id;row[3]=pool.Type==2?"アクターガチャ交換所":"ポスターガチャ交換所";
            row[4]=1;row[5]=pool.PointId;row[6]=pool.ExchangeBanner;row[7]=Start;row[8]=End;
            row[10]=pool.Entries.Where(e=>e.Rarity==pool.TopRarity).Select((e,i)=>new object?[] {
                pool.ExchangeId(e),e.MasterId,e.Type,1,null,i+1,null,false,null,null,Start,End,pool.PointId,ExchangeCost}).ToArray();
            return (object?)row;
        })).ToArray();
    }
    public static object?[] MasterItems(object?[] originals)
    {
        var rows=originals.Cast<object?[]>().Select(row=> {
            if(!Pools.Any(pool=>pool.PointId==Long(row[0])))return (object?)row;
            var copy=(object?[])row.Clone();copy[4]=null;copy[5]=int.MaxValue;return copy;
        }).ToList();
        return rows.ToArray();
    }
    static readonly DateTime Start = new(2020,1,1,0,0,0,DateTimeKind.Utc);
    static readonly DateTime End = new(2099,1,1,0,0,0,DateTimeKind.Utc);
    public static long Long(object? value) => Convert.ToInt64(value ?? throw new InvalidDataException("主数据数值缺失"));
    static object?[] ReadRows(string resource)
    {
        using var stream=typeof(GachaRules).Assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException($"缺少抽卡主数据：{resource}");
        using var document=JsonDocument.Parse(stream);
        static object? Read(JsonElement value) => value.ValueKind switch {
            JsonValueKind.Array=>value.EnumerateArray().Select(Read).ToArray(),JsonValueKind.Null=>null,
            JsonValueKind.True=>true,JsonValueKind.False=>false,JsonValueKind.String=>value.GetString(),
            JsonValueKind.Number=>value.TryGetInt64(out var number)?(object)number:value.GetDouble(),
            JsonValueKind.Object when value.TryGetProperty("_type",out var type) && type.GetString()=="timestamp" => DateTimeOffset.Parse(value.GetProperty("value").GetString()!).UtcDateTime,
            _=>throw new InvalidDataException("抽卡主数据字段类型不支持")};
        return (object?[])Read(document.RootElement)!;
    }
}
static class GachaErrors
{
    public const string Unavailable="GACHA_UNAVAILABLE";
    public const string Invalid="GACHA_INVALID_REQUEST";
    public const string Insufficient="GACHA_INSUFFICIENT_CURRENCY";
}
