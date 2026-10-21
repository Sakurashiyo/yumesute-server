// 已核实的基础消耗和奖励规则；协力专属加成暂不叠加。
static class LiveRewardRules
{
    public enum Mode
    {
        Ordinary = 0,
        Ghost = 1,
        Multi = 2,
        Audition = 3
    }

    static readonly int[] RewardMagnifications = [1, 5, 10, 14, 18, 21, 24, 26, 28, 29, 30];
    static readonly Dictionary<long, int> MusicStamina = GachaRules.ReadRows("Progression.MusicMaster.json")
        .Cast<object?[]>().ToDictionary(row => Convert.ToInt64(row[0]), row => Convert.ToInt32(row[13]));
    static readonly Dictionary<long, long> FrameGroups = GachaRules.ReadRows("Live.LiveSettingMaster.json")
        .Cast<object?[]>().ToDictionary(row => Convert.ToInt64(row[0]), row => Convert.ToInt64(row[3]));
    static readonly Dictionary<long, object?[][]> Frames = GachaRules.ReadRows("Live.LiveDropFrameGroupMaster.json")
        .Cast<object?[]>().ToDictionary(row => Convert.ToInt64(row[0]), row => ((object?[])row[1]!).Cast<object?[]>().ToArray());

    public sealed record Snapshot(bool Ordinary, int BaseStamina, int RequestedMagnification,
        int EffectiveMagnification, int StaminaBefore, object?[]? Unit, long SettingId = 1)
    {
        public int StaminaCost => checked(BaseStamina * EffectiveMagnification);
        public int Experience => StaminaCost;
        public decimal QuantityFactor => BaseStamina * (decimal)RewardMagnifications[EffectiveMagnification] / 20m;
        public object?[] ToWire() => [2, Ordinary, BaseStamina, RequestedMagnification, EffectiveMagnification, StaminaBefore, Unit, SettingId];

        public static Snapshot FromWire(object?[] row)
        {
            if (!(row.Length == 7 && Convert.ToInt32(row[0]) == 1 || row.Length == 8 && Convert.ToInt32(row[0]) == 2)
                || row[1] is not bool ordinary
                || Convert.ToInt32(row[2]) < 0 || Convert.ToInt32(row[3]) is < 0 or > 10
                || Convert.ToInt32(row[4]) is < 0 or > 10 || Convert.ToInt32(row[5]) < 0
                || row[6] is not (null or object?[]))
                throw new InvalidDataException("演出开演快照非法");
            var snapshot = new Snapshot(ordinary, Convert.ToInt32(row[2]), Convert.ToInt32(row[3]),
                Convert.ToInt32(row[4]), Convert.ToInt32(row[5]), (object?[]?)row[6], row.Length == 8 ? Convert.ToInt64(row[7]) : 1);
            if (!FrameGroups.ContainsKey(snapshot.SettingId)) throw new InvalidDataException("演出奖励设置不存在");
            if (snapshot.EffectiveMagnification > snapshot.RequestedMagnification
                || snapshot.StaminaCost > snapshot.StaminaBefore)
                throw new InvalidDataException("演出快照消耗不一致");
            if (snapshot.Unit is { } unit)
            {
                if (unit.Length != 12) throw new InvalidDataException("演出队伍快照非法");
                // 修复此前误存为浮点数的整数百分比，保留该场已经扣除的体力依据。
                if (unit[9] is double percentage && double.IsFinite(percentage) && percentage == Math.Truncate(percentage))
                    unit[9] = checked((int)percentage);
                // 通用解码器把映射键读成字符串；LiveUnit 的角色和时轴必须按整数键下发。
                foreach (var index in new[] { 0, 1 })
                    if (unit[index] is Dictionary<string, object?> map)
                        unit[index] = map.ToDictionary(entry => int.Parse(entry.Key,
                            System.Globalization.CultureInfo.InvariantCulture), entry => entry.Value);
                    else if (unit[index] is not Dictionary<int, object?>)
                        throw new InvalidDataException("演出队伍映射非法");
            }
            return snapshot;
        }
    }

    public sealed record Drop(long FrameId, int Type, long MasterId, int Quantity)
    {
        public object?[] ToWire() => [new object?[] { Type, MasterId, Quantity, null, null, null, false }, FrameId, 0, false];
    }

    public static Snapshot Start(long chartId, bool useStamina, int requested, int stamina,
        object?[]? unit, bool ordinary, long settingId = 1)
    {
        if (requested is < 0 or > 10 || stamina < 0 || !FrameGroups.ContainsKey(settingId))
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidStart);
        var baseStamina = ordinary ? MusicStamina[PlayerProgressionRules.Charts[chartId].MusicMasterId] : 0;
        if (baseStamina < 0) throw new InvalidDataException("歌曲基础体力非法");
        var effective = useStamina && baseStamina > 0 ? Math.Min(requested, stamina / baseStamina) : 0;
        return new(ordinary, baseStamina, requested, effective, stamina, unit, settingId);
    }

    public static int Coins(Snapshot snapshot, long score)
    {
        if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
        return checked((int)decimal.Floor(100m * snapshot.QuantityFactor * (1m + score / 5_000_000m)));
    }

    public static int Quantity(Snapshot snapshot, int original, bool unaffected) =>
        original < 0 ? throw new InvalidDataException("掉落候选数量非法")
            : unaffected ? original : checked((int)decimal.Floor(snapshot.QuantityFactor * original));

    public static Drop[] Materials(Snapshot snapshot, long score, DateTime now, Func<int, int> choose)
    {
        var drops = new List<Drop>();
        foreach (var frame in Frames[FrameGroups[snapshot.SettingId]])
        {
            if (frame[8] is DateTime start && now < start || frame[9] is DateTime end && now >= end) continue;
            // AccessoryFormula 没有可验证的开奖概率；历史活动框也不在本次普通材料范围内。
            if (Convert.ToInt32(frame[3]) != 0) continue;
            var eligible = Convert.ToInt32(frame[4]) switch
            {
                0 => true,
                1 => snapshot.EffectiveMagnification > 0,
                2 => snapshot.EffectiveMagnification > 0 && score >= Convert.ToInt64(frame[7]),
                _ => false
            };
            if (!eligible) continue;
            var candidates = ((object?[])frame[6]!).Cast<object?[]>().ToArray();
            if (candidates.Length == 0) throw new InvalidDataException("普通奖励框没有候选");
            // 候选没有独立权重，采用本服等概率选择，不将此规则视为官方概率。
            var selected = candidates[choose(candidates.Length)];
            if (Convert.ToInt32(selected[1]) != 1)
                throw new InvalidDataException("普通材料框出现非道具奖励");
            var quantity = Quantity(snapshot, Convert.ToInt32(selected[2]), (bool)frame[5]!);
            if (quantity > 0) drops.Add(new(Convert.ToInt64(frame[0]), 1, Convert.ToInt64(selected[0]), quantity));
        }
        return drops.ToArray();
    }
}
