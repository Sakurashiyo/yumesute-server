using System.Text.Json;
using Npgsql;

sealed partial class UserDataService
{
// TODO（Live）：将这些目前通过抓包得到的固定常量替换为真正的实时结算计算逻辑。
//
// 官方服务器似乎会计算以下因素：
// - 经验值（EXP）
// - 金币（Coin）
// - 道具掉落（Item Drops）
// - 任务进度（Mission Progress）
// - 体力消耗（Stamina Cost）
// - 等级进度（Rank Progress）
//
// 这些计算依赖于：
// - 实时的 Master Data（Live Master Data）
// - 玩家选择的难度（Selected Difficulty）
// - 队伍的战力与技能（Party Power / Skills）
// - 提交到 /api/Lives/FinishAndValidate 的分数数据包（Score Package）
// - 当前进行中的活动（Active Events）
// - Boost（加成）设置
//
// 在真正的结算计算器完成之前，请仅将这里保留为一个与抓包结果兼容的临时实现（Shim）。

    const int CapturedLiveExpGain = 189;
    const int CapturedLiveCoinGain = 3510;
    static readonly (long ItemMasterId, int Quantity)[] CapturedLiveItemRewards =
    {
        (432607, 189),
        (130021, 105),
        (130023, 35),
        (120001, 35),
        (130011, 60),
        (4235013, 5715)
    };
    static readonly (long ItemMasterId, int Quantity)[] CapturedLeagueItemRewards =
    {
        (120002, 30),
        (130061, 430),
        (130043, 150),
        (130042, 318),
        (130041, 1239)
    };
    static readonly (long ItemMasterId, int Quantity)[] CapturedTripleCastItemRewards =
    {
        (120002, 60),
        (130061, 460),
        (130043, 300),
        (130042, 468),
        (130041, 1389)
    };
    static readonly (long ItemMasterId, int Quantity)[] CapturedGhostLiveItemRewards =
    {
        (120002, 90),
        (130061, 490)
    };
    static readonly (long ItemMasterId, int Quantity)[] CapturedMultiLiveItemRewards =
    {
        (140000, 10),
        (120002, 120),
        (130061, 527),
        (432607, 5395),
        (120001, 385),
        (130023, 1266),
        (130022, 1934),
        (130032, 533),
        (130062, 129),
        (130034, 192),
        (4235013, 122861)
    };
    static readonly (long ItemMasterId, int Quantity)[] CapturedSecondMultiLiveItemRewards =
    {
        (120002, 150),
        (130061, 557),
        (140000, 20),
        (432607, 5425),
        (130062, 136),
        (120001, 392),
        (130022, 1971),
        (130015, 2),
        (4235013, 129065)
    };


    static async Task UpsertItemQuantityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        long itemMasterId,
        int quantity)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into user_item_possessions (id, "userId", item_master_id, quantity)
            values ($1, $2, $3, $4)
            on conflict ("userId", item_master_id)
            do update set quantity = user_item_possessions.quantity + excluded.quantity
            """,
            UserScopedId(userId, itemMasterId),
            userId,
            itemMasterId,
            quantity);
    }
}
