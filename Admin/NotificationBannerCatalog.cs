using System.Text.Json;

sealed class NotificationBannerCatalog(LocalConfig config)
{
    public sealed record Banner(string Path, string Name, string Key);
    static readonly Banner[] Source = Load();
    static Banner[] Load()
    {
        using var stream = typeof(NotificationBannerCatalog).Assembly.GetManifestResourceStream("AdminWeb.notification-banners.json")
            ?? throw new InvalidOperationException("缺少公告横幅目录");
        return JsonSerializer.Deserialize<Banner[]>(stream, GameOperationsJson.Options)
            ?? throw new InvalidDataException("公告横幅目录为空");
    }
    public Banner[] Available() => Source.Where(banner =>
        File.Exists(System.IO.Path.Combine(config.ResourcesRoot, "Textures", "Banners", banner.Path + ".astc.gz"))
        && File.Exists(PreviewFile(banner))).ToArray();
    public static string PreviewFile(Banner banner) => System.IO.Path.Combine(AppContext.BaseDirectory, "AdminIcons", "banner", banner.Key + ".png");
}
