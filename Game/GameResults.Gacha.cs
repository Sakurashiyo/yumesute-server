static partial class GameResults
{
    public static object?[] GachaListResult() => GachaRules.Pools.Select(pool=>(object?)new object?[] {
        pool.Id,Array.Empty<object?>(),7,6}).ToArray();
    public static object?[] CharacterLineupResult(long poolId)=>GachaRules.Lineup(poolId,2);
    public static object?[] PosterLineupResult(long poolId)=>GachaRules.Lineup(poolId,3);
}
