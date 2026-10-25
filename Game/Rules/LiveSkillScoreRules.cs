// 只复算没有动态效果的技能；倍率与演技力均取本场开演快照。
static class LiveSkillScoreRules
{
    public static long Calculate(int performance, int percentage, int life)
    {
        if (performance < 0 || percentage < 0 || life < 0)
            throw new ArgumentOutOfRangeException(nameof(performance));
        return checked((long)(performance * (percentage / 100d) * (life > 0 ? 1d : 0.1d)));
    }

    public static bool CanRecalculate(object?[] unit, object?[] finish)
    {
        if (finish.Length != 10 || finish[2] is not null) return false;
        // 带动态效果（含协力附加分）的技能分不能安全复算：保留客户端数值，但由调用方按分数上界校验。
        if (LiveUnitRules.Rows(unit[3]).Length > 0) return false;
        foreach (var sense in LiveUnitRules.Rows(unit[2]))
        {
            if (new[] { 6, 8, 9, 10 }.Any(index => ((object?[])sense[index]!).Length > 0))
                return false;
            var effect = (object?[])sense[7]!;
            if (LiveUnitRules.I(effect[1]) != 0 || LiveUnitRules.I(effect[2]) != 0
                || LiveUnitRules.Rows(effect[3]).Length > 0) return false;
        }
        var starAct = (object?[])unit[4]!;
        return LiveUnitRules.I(starAct[2]) == 0 && LiveUnitRules.Rows(starAct[3]).Length == 0;
    }

    public static object?[] Recalculate(object?[] unit, object?[] finish)
    {
        _ = LiveFinishValidation.Normalize(finish);
        if (!CanRecalculate(unit, finish))
            throw new InvalidOperationException("当前演出包含尚未支持的技能效果或结算格式");
        var senses = LiveUnitRules.Rows(unit[2]).ToDictionary(row => LiveUnitRules.N(row[0]));
        var actors = ((Dictionary<int, object?>)unit[0]!).Values.Cast<object?[]>()
            .ToDictionary(row => LiveUnitRules.N(row[0]));
        var timings = (Dictionary<int, object?>)unit[1]!;
        var starAct = (object?[])unit[4]!;
        var seenSenses = new HashSet<(int Second, long SenseId)>();
        var seenStars = new HashSet<int>();
        var copy = (object?[])finish.Clone();
        foreach (var field in new[] { 5, 6 })
        {
            if (finish[field] is not object?[] blocks) continue;
            copy[field] = blocks.Cast<object?[]>().Select(block =>
            {
                var time = LiveUnitRules.N(block[3]);
                if (time is < 0 or > int.MaxValue || !timings.TryGetValue((int)time, out var timingValue))
                    throw new BadHttpRequestException(LiveProgressionErrors.InvalidFinish);
                var timing = (object?[])timingValue!;
                int performance, percentage;
                if (field == 5)
                {
                    var id = LiveUnitRules.N(block[4]);
                    if (!seenSenses.Add(((int)time, id)) || !senses.TryGetValue(id, out var sense)
                        || !((object?[])timing[5]!).Any(value => LiveUnitRules.N(value) == id))
                        throw new BadHttpRequestException(LiveProgressionErrors.InvalidFinish);
                    var actor = actors[LiveUnitRules.N(sense[1])];
                    performance = LiveUnitRules.I(((object?[])actor[8]!)[3]);
                    percentage = LiveUnitRules.I(((object?[])sense[7]!)[0]);
                }
                else
                {
                    if (!seenStars.Add((int)time) || timing[2] is not true)
                        throw new BadHttpRequestException(LiveProgressionErrors.InvalidFinish);
                    performance = LiveUnitRules.I(unit[5]);
                    percentage = LiveUnitRules.I(starAct[0]);
                }
                // 不补发未提交事件，也不改写生命、连击、哈希或协力附加分。
                var row = (object?[])block.Clone();
                row[1] = Calculate(performance, percentage, LiveUnitRules.I(block[2]));
                return (object?)row;
            }).ToArray();
        }
        return copy;
    }
}
