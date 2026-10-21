using System.Text.Json;
using System.Text.Json.Serialization;

sealed record GameOperationsConfiguration(MissionPassSettings MissionPass,
    Dictionary<long, bool> Gachas, Dictionary<long, LoginBonusSettings> LoginBonuses)
{
    public static GameOperationsConfiguration Default() => new(
        new(true, true, MissionPassRules.Start, null, MissionPassRules.Stages, MissionPassRules.LoopRewards),
        GachaRules.AllPools.ToDictionary(pool => pool.Id, pool => !pool.Historical),
        LoginBonusRules.Rewards.ToDictionary(reward => reward.Id,
            reward => new LoginBonusSettings(reward.Id == 10006, reward.StartAt, null, reward.ThingType, reward.MasterId, reward.Quantity, reward.Days, reward.IsLoop)));
}

sealed record MissionPassSettings(bool Enabled, bool SpEnabled, DateTimeOffset StartsAt, DateTimeOffset? EndsAt,
    MissionPassRules.Stage[] Stages, MissionPassRules.Reward[][] LoopRewards)
{
    public bool IsOpen(DateTimeOffset now) => Enabled && StartsAt <= now && now < (EndsAt ?? MissionPassRules.PermanentEnd);
}
sealed record LoginBonusSettings(bool Enabled, DateTimeOffset StartsAt, DateTimeOffset? EndsAt,
    int? ThingType = null, long? MasterId = null, int? Quantity = null, int? Days = null, bool? IsLoop = null)
{
    public bool IsOpen(DateTimeOffset now) => Enabled && StartsAt <= now && now < (EndsAt ?? MissionPassRules.PermanentEnd);
}
sealed record GameOperationsSnapshot(long Revision, GameOperationsConfiguration Configuration);

static class GameOperationsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}

static class GameOperationsErrors
{
    public const string Invalid = "OPERATIONS_INVALID_CONFIG";
}
sealed class GameOperationsException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
