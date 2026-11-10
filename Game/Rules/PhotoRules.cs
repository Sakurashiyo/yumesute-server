using System.Text.Json;

static class PhotoErrors
{
    public const string InvalidRequest = "PHOTO_INVALID_REQUEST";
    public const string InvalidImage = "PHOTO_INVALID_IMAGE";
    public const string InvalidFilm = "PHOTO_INVALID_FILM";
    public const string FilmInsufficient = "PHOTO_FILM_INSUFFICIENT";
    public const string CapacityReached = "PHOTO_CAPACITY_REACHED";
    public const string NotFound = "PHOTO_NOT_FOUND";
}

static class PhotoRules
{
    public static readonly IReadOnlyDictionary<long, long?> Films = Load();
    public static readonly HashSet<long> CharacterIds = LoadCharacters();
    static HashSet<long> LoadCharacters()
    {
        using var stream = typeof(PhotoRules).Assembly.GetManifestResourceStream("InventoryMaster.CharacterBaseMaster.json")
            ?? throw new InvalidOperationException("缺少角色基础主数据");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(row => row[0].GetInt64()).ToHashSet();
    }
    static IReadOnlyDictionary<long, long?> Load()
    {
        using var stream = typeof(PhotoRules).Assembly.GetManifestResourceStream("Photo.FilmItemMaster.json")
            ?? throw new InvalidOperationException("缺少胶卷主数据");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().ToDictionary(row => row[1].GetInt64(),
            row => row[2].ValueKind == JsonValueKind.Null ? (long?)null : row[2].GetInt64());
    }
}
