static class AuditionRules
{
    public sealed record Master(long Id, long MusicId, long SenseNotationId, int MaxPhase, DateTime Start, DateTime End);
    public sealed record Phase(int Number, long Score, int StarActs, long RewardPackageId);
    public sealed record Reward(long MasterId, int Type, int Quantity);
    public static readonly IReadOnlyDictionary<long, Master> Masters = Rows("AuditionMaster")
        .ToDictionary(row => N(row[0]), row => new Master(N(row[0]), N(row[1]), N(row[4]),
            int.Parse((string)row[5]!, System.Globalization.CultureInfo.InvariantCulture), (DateTime)row[6]!, (DateTime)row[7]!));
    public static readonly IReadOnlyDictionary<long, Phase[]> Phases = Rows("AuditionPhaseMaster")
        .GroupBy(row => N(row[1])).ToDictionary(group => group.Key, group => group
            .Select(row => new Phase(checked((int)N(row[2])), N(row[4]), row[5] is null ? 0 : checked((int)N(row[5])), N(row[6])))
            .OrderBy(phase => phase.Number).ToArray());
    public static readonly IReadOnlyDictionary<long, Reward[]> Rewards = Rows("AuditionRewardPackageMaster")
        .ToDictionary(row => N(row[0]), row => ((object?[])row[1]!).Cast<object?[]>()
            .Select(reward => new Reward(N(reward[0]), checked((int)N(reward[1])), checked((int)N(reward[2])))).ToArray());

    static AuditionRules()
    {
        foreach (var master in Masters.Values)
            if (!Phases.TryGetValue(master.Id, out var phases)
                || !phases.Select(phase => phase.Number).SequenceEqual(Enumerable.Range(1, master.MaxPhase))
                || phases.Any(phase => phase.Score < 0 || phase.StarActs < 0 || !Rewards.ContainsKey(phase.RewardPackageId))
                || !LiveUnitRules.HasSenseNotation(master.SenseNotationId))
                throw new InvalidDataException($"公演主数据不完整：{master.Id}");
        if (Rewards.Values.SelectMany(rewards => rewards).Any(reward => reward.Quantity <= 0 || reward.Type is not (1 or 13)))
            throw new InvalidDataException("公演奖励主数据包含未支持的类型或数量");
    }

    static long N(object? value) => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    static IEnumerable<object?[]> Rows(string name) => GachaRules.ReadRows($"Competition.{name}.json").Cast<object?[]>();
    public static bool Exists(long auditionId) => Masters.ContainsKey(auditionId);
    public static Master Get(long auditionId) => Masters.TryGetValue(auditionId, out var master)
        ? master : throw new BadHttpRequestException(AuditionErrors.NotFound, StatusCodes.Status404NotFound);
    public static int ClearedPhase(long auditionId, long score, int starActs) =>
        Phases[auditionId].TakeWhile(phase => score >= phase.Score && starActs >= phase.StarActs)
            .Select(phase => phase.Number).DefaultIfEmpty(0).Max();
}

static class AuditionErrors
{
    public const string NotFound = "AUDITION_NOT_FOUND";
    public const string InvalidStart = "AUDITION_INVALID_START";
    public const string PartyMissing = "AUDITION_PARTY_NOT_FOUND";
    public const string PartyUnavailable = "AUDITION_PARTY_UNAVAILABLE";
}
