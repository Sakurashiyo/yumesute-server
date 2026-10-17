static partial class LiveUnitRules
{
    static double EffectValue(object?[] effect, int level) =>
        Convert.ToDouble(Rows(effect[4]).Single(row => I(row[0]) == level)[1]);

    static object?[] ParseEffects(object?[][] orders, int level, Member owner, Member[] members) =>
        orders.Select(order => (object?)ParseEffect(I(order[0]), Effects[N(order[1])], level, owner, members)).ToArray();

    static object?[] ParseEffect(int order, object?[] effect, int level, Member owner, Member[] members)
    {
        var value = EffectValue(effect, level) / (I(effect[3]) == 3 ? 1d : 100d);
        var targets = I(effect[2]) == 0 ? new object?[] { new object?[] { null, value } } :
            (I(effect[2]) == 2 ? members : new[] { owner })
            .Where(member => Rows(effect[5]).All(condition => MatchesEffectCondition(condition, member, owner)))
            .Select(member => (object?)new object?[] { member.Card[0], value })
            .ToArray();
        return new object?[]
        {
            order, effect[0], effect[1],
            Rows(effect[7]).Select(trigger => (object?)new object?[] { trigger[0], trigger[1] ?? 0L }).ToArray(),
            targets, effect[6]
        };
    }

    static bool MatchesEffectCondition(object?[] condition, Member target, Member owner)
    {
        var master = Cards[N(target.Card[1])];
        return I(condition[0]) switch
        {
            1 => N(master[1]) == N(condition[1]),
            2 => N(Bases[N(master[1])][9]) == N(condition[1]),
            3 => N(master[6]) == N(condition[1]) || master[27] is not null && N(master[27]) == N(condition[1]),
            4 => N(Senses[N(master[11])][3]) == N(condition[1]),
            5 => N(master[0]) == N(condition[1]),
            7 => Math.Abs(target.Slot - owner.Slot) == N(condition[1]),
            8 => ((object?[])Groups[N(condition[1])][1]!).Any(id => N(id) == N(master[1])),
            _ => throw new InvalidOperationException($"尚未实现的开演效果目标条件：{condition[0]}")
        };
    }

    static int RuntimeCondition(int type) => type is 2 or 3 or 4 or 5 or 6 or 11 ? 0 : type;

    static object?[] ParseBranches(object?[][] branches, int level, Member owner, Member[] members, int condition, object? parameter)
    {
        var selected = branches.AsEnumerable();
        if (condition != 0 && RuntimeCondition(condition) == 0)
        {
            var masters = members.Select(member => Cards[N(member.Card[1])]).ToArray();
            var value = condition switch
            {
                2 => owner.Slot,
                3 => masters.Count(master => N(Bases[N(master[1])][9]) == N(parameter)),
                4 => masters.Select(master => N(Bases[N(master[1])][9])).Distinct().Count(),
                5 => masters.Count(master => N(master[6]) == N(parameter)),
                6 => masters.Select(master => N(master[6])).Distinct().Count(),
                11 => masters.Count(master => ((object?[])Groups[N(parameter)][1]!).Any(id => N(id) == N(master[1]))),
                _ => throw new InvalidOperationException("非法编队分支条件")
            };
            selected = selected.Where(branch => I(branch[2]) switch
            {
                0 => true, 1 => value == N(branch[3]), 2 => value >= N(branch[3]),
                3 => value <= N(branch[3]), 4 => value != N(branch[3]),
                _ => throw new InvalidOperationException("非法分支比较方式")
            });
        }
        return selected.Select(branch => (object?)new object?[]
        {
            RuntimeCondition(condition) == 0 ? null : branch[3],
            RuntimeCondition(condition) == 0 ? null : branch[2],
            ParseEffects(Rows(branch[1]), level, owner, members)
        }).ToArray();
    }
}
