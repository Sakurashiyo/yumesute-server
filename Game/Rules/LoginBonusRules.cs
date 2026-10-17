static class LoginBonusRules
{
    public sealed record Reward(long Id, string Title, int ThingType, long MasterId, int Quantity,
        int Days, bool IsLoop, int DisplayOrder, int DisplayCategory, DateTimeOffset StartAt, DateTimeOffset EndAt);
    static readonly TimeSpan Offset = TimeSpan.FromHours(8);
    public static readonly Reward[] Rewards =
    [
        new(168, "「最終章」追加記念アクターガチャ10連チケットログインボーナス", 1, 210446, 1, 10, false, 30, 2, new(2026,7,21,1,0,0,Offset), new(2026,8,10,13,0,0,Offset)),
        new(169, "「最終章」追加記念ポスターガチャ10連チケットログインボーナス", 1, 211432, 1, 10, false, 40, 2, new(2026,7,21,1,0,0,Offset), new(2026,8,10,13,0,0,Offset)),
        new(170, "「最終章」追加記念ログインボーナス", 13, 0, 1000, 7, false, 50, 2, new(2026,7,21,1,0,0,Offset), new(2026,8,10,13,0,0,Offset)),
        new(179, "3rd Anniversaryカウントダウンログインボーナス", 1, 210001, 3, 1, false, 1, 2, new(2026,7,22,8,0,0,Offset), new(2026,7,23,8,0,0,Offset)),
        new(10006, "ログインボーナス", 13, 0, 300, 15, true, 1000, 1, new(2026,7,17,23,0,0,Offset), DateTimeOffset.MaxValue),
        new(35013, "最終章 ログインボーナス", 1, 111103, 5, 7, false, 100, 2, new(2026,7,21,1,0,0,Offset), new(2026,7,31,13,0,0,Offset))
    ];
}
