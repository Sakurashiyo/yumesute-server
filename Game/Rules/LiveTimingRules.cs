// 技能时轴使用歌曲时长、实际槽位、CT 和队长光需求，不能沿用示例队伍的触发结果。
static class LiveTimingRules
{
    public static Dictionary<int, object?> Calculate(int duration, object?[][] senses, int[] required, object?[][]? notation = null)
    {
        if (duration <= 0 || required.Length != 5 || required.Any(count => count < 0))
            throw new InvalidOperationException("歌曲长度或 Star Act 光需求无效");
        var byPosition = senses.ToDictionary(sense => LiveUnitRules.I(sense[0]));
        var events = notation is null
            ? UniformEvents(duration)
            : notation.Select(row => (Slot: LiveUnitRules.I(row[1]), Second: LiveUnitRules.I(row[2])))
                .OrderBy(item => item.Second).ToArray();
        if (events.Any(item => item.Slot is < 1 or > 5 || item.Second <= 0 || item.Second > duration))
            throw new InvalidOperationException("指定技能谱面事件无效");
        var last = new Dictionary<int, int>();
        var lights = new List<int>();
        var timings = new Dictionary<int, object?>();
        foreach (var (position, second) in events)
        {
            if (!byPosition.TryGetValue(position, out var sense)) continue;
            var acquired = ((object?[])sense[5]!).Select(LiveUnitRules.I).ToArray();
            if (last.TryGetValue(position, out var previous) && second - previous < LiveUnitRules.I(sense[3]))
            {
                // 客户端 CT 不足时清空已存光，本次也不触发 Sense。
                lights.Clear();
                timings.Add(second, new object?[]
                {
                    Array.Empty<object?>(), Array.Empty<object?>(), false, true,
                    acquired.Cast<object?>().ToArray(), Array.Empty<object?>(), Array.Empty<object?>(), sense[11]
                });
                continue;
            }
            last[position] = second;
            var grant = new List<int>();
            foreach (var light in acquired)
            {
                // 固定光超出需求时不再累积；多余光可满足队长的自由光需求。
                if (light is < 0 or > 4) throw new InvalidOperationException("技能光类型无效");
                if (light == 0 || lights.Count(l => l == light) < required[light]
                    || lights.Count - Enumerable.Range(1, 4).Sum(type => Math.Min(required[type], lights.Count(l => l == type))) < required[0])
                {
                    lights.Add(light);
                    grant.Add(light);
                }
            }
            var missing = Enumerable.Range(1, 4).Sum(type => Math.Max(0, required[type] - lights.Count(l => l == type)));
            var availableFree = lights.Count(l => l == 0);
            var fire = required.Sum() > 0 && missing <= availableFree && lights.Count >= required.Sum();
            if (fire) lights.Clear();
            timings.Add(second, new object?[]
            {
                lights.Cast<object?>().ToArray(), grant.Cast<object?>().ToArray(), fire, false,
                acquired.Cast<object?>().ToArray(), new object?[] { sense[0] }, Array.Empty<object?>(), sense[11]
            });
        }
        return timings;
    }

    static (int Slot, int Second)[] UniformEvents(int duration)
    {
        var positions = new[] { 1, 2, 3, 4, 5, 3, 2, 1 };
        var step = duration / (positions.Length + 1);
        if (step <= 0) throw new InvalidOperationException("歌曲长度无效");
        return positions.Select((slot, index) => (slot, step * (index + 1))).ToArray();
    }
}
