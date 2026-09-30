static partial class GameResults
{
    public static object?[] StoryEventMyRanking(long eventMasterId)
    {
        if (eventMasterId == 35013)
        {
            return new object?[] { 6357, 48638, "5444921718", 0 };
        }

        return new object?[] { 5034, 0, "000000", 0 };
    }

    public static object?[] StoryEventRankingList()
    {
        return new object?[] { Array.Empty<object?>(), Array.Empty<object?>() };
    }

    public static object?[] StoryEventReadTipsPresentData(long eventMasterId)
    {
        return new object?[]
        {
            new object?[] { 101, new object?[] { Random.Shared.Next(3000000, 3999999), eventMasterId, 0, DateTime.UtcNow, null, true, 1 } }
        };
    }

    public static object?[] ComicReadRewardResult()
    {
        return new object?[]
        {
            new object?[] { 13, 0, 30, null, null, null, false }
        };
    }
}
