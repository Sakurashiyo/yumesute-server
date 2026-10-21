using System.Globalization;

// 开演协议与主数据组装独立于持久化，避免继续返回抓包中的示例角色和战力。
static partial class LiveUnitRules
{
    public sealed record Member(int Slot, object?[] Card, object?[] Status);
    static readonly Dictionary<long, object?[]> Cards = Table("InventoryMaster.CharacterMaster.json");
    static readonly Dictionary<long, object?[]> Senses = Table("Live.SenseMaster.json");
    static readonly Dictionary<long, object?[]> StarActs = Table("Live.StarActMaster.json");
    static readonly Dictionary<long, object?[]> Conditions = Table("Live.StarActConditionMaster.json");
    static readonly Dictionary<long, object?[]> Leaders = Table("Live.LeaderSenseMaster.json");
    static readonly Dictionary<long, object?[]> Effects = Table("Status.EffectMaster.json");
    static readonly Dictionary<long, object?[]> Music = Table("Progression.MusicMaster.json");
    static readonly Dictionary<long, object?[]> Bases = Table("InventoryMaster.CharacterBaseMaster.json");
    static readonly Dictionary<long, object?[]> Groups = Table("Live.EffectTriggerCharacterBaseGroupMaster.json");
    static readonly Dictionary<long, object?[]> Blooms = Table("Status.CharacterBloomBonusGroupMaster.json");
    static readonly Dictionary<long, object?[]> SenseNotations = Table("Live.SenseNotationMaster.json");

    static Dictionary<long, object?[]> Table(string name) =>
        GachaRules.ReadRows(name).Cast<object?[]>().ToDictionary(row => N(row[0]));
    internal static int I(object? value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);
    internal static long N(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);
    internal static object?[][] Rows(object? value) => ((object?[])value!).Cast<object?[]>().ToArray();
    internal static bool HasSenseNotation(long id) => SenseNotations.ContainsKey(id);

    public static object?[] Build(Member[] members, int leaderSlot, long chartId, bool auto, long? senseNotationId = null)
    {
        if (members.Length is < 1 or > 5 || members.Any(m => m.Slot is < 1 or > 5)
            || members.Select(m => m.Slot).Distinct().Count() != members.Length
            || members.Select(m => N(m.Card[0])).Distinct().Count() != members.Length
            || members.All(m => m.Slot != leaderSlot))
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidStart);
        var leader = members.Single(m => m.Slot == leaderSlot);
        var leaderMaster = Cards[N(leader.Card[1])];
        var actors = members.ToDictionary(m => m.Slot, m => (object?)new object?[]
        {
            m.Card[0], Cards[N(m.Card[1])][1], m.Card[1], m.Card[5], m.Card[4], m.Slot, m.Card[7],
            m.Status.ToArray(), m.Status.ToArray(), m.Card[10], null, 0, m.Card[13]
        });
        var passives = members.ToDictionary(m => m.Slot, m => Rows(Blooms[N(Cards[N(m.Card[1])][13])][1])
            .Where(bloom => I(bloom[3]) <= I(m.Card[4])).Select(bloom => Effects[N(bloom[5])]).ToList());
        if (leaderMaster[22] is not null)
            foreach (var detail in Rows(Leaders[N(leaderMaster[22])][2]))
                foreach (var member in members.Where(m => MatchesCategories(m.Card, Rows(detail[1]))))
                    passives[member.Slot].Add(Effects[N(detail[0])]);
        foreach (var member in members)
        {
            var actor = (object?[])actors[member.Slot]!;
            actor[8] = ApplyStatus(member.Status, passives[member.Slot]);
        }
        var senses = members.Select(member =>
        {
            var master = Cards[N(member.Card[1])];
            var sense = Senses[N(master[11])];
            var level = I(member.Card[7]);
            var type = I(sense[3]);
            var lights = Enumerable.Repeat<object?>(type, I(sense[9])).ToArray();
            if (type == 9) lights = Array.Empty<object?>();
            var additions = passives[member.Slot].Where(effect => I(effect[1]) is >= 13 and <= 18)
                .SelectMany(effect => Enumerable.Repeat<object?>(I(effect[1]) switch
                {
                    13 => type, 14 => 0, _ => I(effect[1]) - 14
                }, checked((int)EffectValue(effect, 1))));
            lights = lights.Concat(additions).ToArray();
            var coolTime = Math.Max(0, I(sense[10]) - (int)passives[member.Slot]
                .Where(effect => I(effect[1]) == 20).Sum(effect => EffectValue(effect, 1)));
            var id = (long)member.Slot;
            return new object?[]
            {
                id, member.Card[0], null, coolTime, sense[3], lights,
                ParseEffects(Rows(sense[4]), level, member, members),
                new object?[] { checked(I(sense[7]) + (level - 1) * I(sense[8])), sense[6],
                    RuntimeCondition(I(sense[11])), ParseBranches(Rows(sense[5]), level, member, members, I(sense[11]), sense[12]) },
                Array.Empty<object?>(), Array.Empty<object?>(), Array.Empty<object?>(),
                sense[0], member.Card[0]
            };
        }).ToArray();
        var starMasterId = I(leader.Card[5]) > 0 && leaderMaster[10] is not null ? N(leaderMaster[10]) : N(leaderMaster[9]);
        var star = StarActs[starMasterId];
        var required = Conditions[N(star[4])];
        var requiredLights = required.Skip(1).Select(I).ToArray();
        foreach (var effect in passives[leaderSlot].Where(effect => I(effect[1]) is >= 42 and <= 45))
        {
            var index = I(effect[1]) - 41;
            requiredLights[index] = Math.Max(0, requiredLights[index] - (int)EffectValue(effect, 1));
        }
        object?[][]? notation = null;
        if (senseNotationId is > 0)
        {
            if (!SenseNotations.TryGetValue(senseNotationId.Value, out var notationMaster))
                throw new BadHttpRequestException(LiveProgressionErrors.InvalidStart);
            notation = Rows(notationMaster[1]);
        }
        var timing = LiveTimingRules.Calculate(I(Music[PlayerProgressionRules.Charts[chartId].MusicMasterId][14]),
            senses, requiredLights, notation);
        var starAct = new object?[]
        {
            checked(I(star[5]) + I(leader.Card[4]) * I(star[6])), leader.Card[0], RuntimeCondition(I(star[9])),
            ParseEffects(Rows(star[7]), 1, leader, members)
                .Select(effect => (object?)new object?[] { null, null, new object?[] { effect } })
                .Concat(ParseBranches(Rows(star[8]), 1, leader, members, I(star[9]), star[10])).ToArray()
        };
        var startEffects = members.SelectMany(member => passives[member.Slot]
                .Where(effect => I(effect[8]) == 3)
                .Select(effect => ParseEffect(0, effect, 1, member, members)))
            .ToArray();
        return new object?[]
        {
            actors, timing, senses, startEffects, starAct,
            checked(actors.Values.Cast<object?[]>().Sum(actor => I(((object?[])actor[8]!)[3]))),
            requiredLights.Sum(), 1000, false,
            checked((int)(passives.Values.SelectMany(effects => effects).Where(effect => I(effect[1]) == 32)
                .Sum(effect => EffectValue(effect, 1)) / 100d)),
            LiveBaseScoreRules.Coefficient(PlayerProgressionRules.Charts[chartId].Difficulty, auto),
            Random.Shared.NextInt64(1, long.MaxValue)
        };
    }

    static bool MatchesCategories(object?[] card, object?[][] alternatives)
    {
        var categories = Rows(Cards[N(card[1])][21]).Where(row => row[1] is false || I(card[5]) > 0)
            .Select(row => N(row[0])).ToHashSet();
        return alternatives.Length == 0 || alternatives.Any(row => row.Where(id => id is not null).All(id => categories.Contains(N(id))));
    }

    static object?[] ApplyStatus(object?[] original, List<object?[]> effects)
    {
        var values = original.Take(3).Select(I).ToArray();
        for (var index = 0; index < 3; index++)
        {
            var applicable = effects.Where(effect => I(effect[1]) == index + 5).ToArray();
            var percent = applicable.Where(e => I(e[3]) == 1).Sum(e => EffectValue(e, 1));
            var fixedValue = applicable.Where(e => I(e[3]) == 3).Sum(e => EffectValue(e, 1));
            values[index] = checked((int)(values[index] * (1 + percent / 10000d) + fixedValue));
        }
        var performance = effects.Where(effect => I(effect[1]) == 11).Sum(e => EffectValue(e, 1));
        return new object?[] { values[0], values[1], values[2], checked((int)(values.Sum() * (1 + performance / 10000d))) };
    }
}
