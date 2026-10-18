// 基础音符分与技能分分开核算，使用开演时的真实编队数值。
static class LiveBaseScoreRules
{
    public static double Coefficient(int difficulty, bool auto) => auto ? 0d : difficulty switch
    {
        1 => 0.95d,
        2 => 0.97d,
        _ => 1d
    };

    public static long[] Calculate(int totalStatus, double coefficient, double basePercentage, IReadOnlyList<int> timings)
    {
        if (totalStatus < 0 || !double.IsFinite(coefficient) || coefficient < 0
            || !double.IsFinite(basePercentage) || basePercentage < 0 || timings.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(totalStatus));
        var maximum = checked((long)(10d * totalStatus * coefficient * (1d + basePercentage / 100d)));
        var increments = new decimal[7];
        for (var timing = 1; timing <= 6; timing++)
            increments[timing] = (timing switch { 6 => 1.01m, 5 => 1m, 4 => 0.8m, 3 => 0.5m, _ => 0m }) / timings.Count;
        var result = new long[timings.Count];
        decimal achieved = 0;
        long issued = 0;
        for (var index = 0; index < timings.Count; index++)
        {
            var timing = timings[index];
            if (timing is < 1 or > 6) throw new BadHttpRequestException(LiveProgressionErrors.InvalidJudges);
            achieved += increments[timing];
            var units = Math.Min(10000m, decimal.Floor(achieved * 10000m));
            var accumulated = checked((long)decimal.Truncate(maximum * units / 10000m));
            result[index] = accumulated - issued;
            issued = accumulated;
        }
        return result;
    }

    public static bool CanRecalculate(object?[] unit, object?[] finish)
    {
        if (finish.Length != 10 || finish[4] is not object?[] { Length: > 0 } notes
            || finish[7] is object?[] { Length: > 0 }) return false;
        return notes.Cast<object?[]>().All(row => Convert.ToInt64(row[2]) > 0)
            && !HasDynamicBaseScore(unit);
    }

    static bool HasDynamicBaseScore(object? value)
    {
        if (value is not object?[] rows) return false;
        if (rows.Length == 6 && rows[2] is not (null or object?[]) && Convert.ToString(rows[2]) == "32"
            && rows[4] is object?[]) return true;
        return rows.Any(HasDynamicBaseScore);
    }

    public static object?[] Recalculate(object?[] unit, object?[] finish)
    {
        var notes = (object?[])finish[4]!;
        var scores = Calculate(Convert.ToInt32(unit[5]), Convert.ToDouble(unit[10]), Convert.ToDouble(unit[9]),
            notes.Cast<object?[]>().Select(row => Convert.ToInt32(row[4])).ToArray());
        var copy = (object?[])finish.Clone();
        copy[4] = notes.Select((value, index) =>
        {
            var row = (object?[])((object?[])value!).Clone();
            row[1] = scores[index];
            return (object?)row;
        }).ToArray();
        return copy;
    }
}
