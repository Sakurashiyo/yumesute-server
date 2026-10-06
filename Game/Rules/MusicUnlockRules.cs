using System.Text.Json;

static class MusicUnlockRules
{
    public const long TicketId = 130001;
    public sealed record Music(long Id, int OwnershipCondition, int StellaCondition, int StellaValue, int OlivierLevel);
    public static readonly IReadOnlyDictionary<long, Music> Musics = ReadMusics();

    static Dictionary<long, Music> ReadMusics()
    {
        using var stream = typeof(MusicUnlockRules).Assembly.GetManifestResourceStream("Progression.MusicMaster.json")
            ?? throw new InvalidOperationException("缺少歌曲解锁主数据");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().ToDictionary(row => row[0].GetInt64(), row =>
        {
            var id = row[0].GetInt64();
            var charts = PlayerProgressionRules.Charts.Values.Where(chart => chart.MusicMasterId == id).ToArray();
            var stella = charts.SingleOrDefault(chart => chart.Difficulty == 4);
            var olivier = charts.SingleOrDefault(chart => chart.Difficulty == 5);
            return new Music(id, row[21].GetInt32(), stella?.UnlockCondition ?? 0, stella?.UnlockValue ?? 0, olivier?.Level ?? 0);
        });
    }

    public static bool StellaReleased(Music music, bool owned, bool stored, int rank, int olivierCount) =>
        owned && (stored || music.StellaCondition == 0 || rank >= 20 || olivierCount >= 20);

    public static int OlivierStatus(Music music, bool owned, bool stella, int stored, int maxClearedLevel) =>
        stored == 3 ? 3 : music.OlivierLevel == 0 || !owned || !stella ? 0 :
        maxClearedLevel >= music.OlivierLevel ? 2 : stored == 1 ? 1 : 0;
}

static class MusicUnlockErrors
{
    public const string InvalidRequest = "MUSIC_INVALID_REQUEST";
    public const string Locked = "MUSIC_DIFFICULTY_LOCKED";
    public const string NotOwned = "MUSIC_NOT_OWNED";
    public const string NotPurchasable = "MUSIC_NOT_PURCHASABLE";
    public const string InsufficientTickets = "MUSIC_INSUFFICIENT_TICKETS";
}