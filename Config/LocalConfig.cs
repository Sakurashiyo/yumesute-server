sealed record LocalConfig(
    string Host,
    int Port,
    string PublicBaseUrl,
    string ApplicationVersion,
    string AssetVersion,
    string MinApplicationVersion,
    string MinAssetVersion,
    string MasterDataVersion,
    long MasterDataPublishTimestamp,
    string MasterDataFile,
    string MasterDataRemotePath,
    string ResourcesRoot,
    string AddressablesRoot,
    string StaticContentUrl,
    string DatabaseUrl,
    string TokenSecret,
    string MsgPackResponseMode,
    string RegisterResponseMode)
{
    public string? ResourcesBaseUrl { get; init; }
    public string? AddressablesBaseUrl { get; init; }

    public int RealtimePort
    {
        get
        {
            var configured = Optional("REALTIME_PORT");
            var port = configured is null ? checked(Port + 1) : ParsePort("REALTIME_PORT", configured);
            if (port is < 1 or > 65535 || port == Port)
                throw new InvalidOperationException("REALTIME_PORT 必须为 1–65535 且不同于 HTTP API 端口");
            return port;
        }
    }

    public string RealtimeBaseUrl => Optional("REALTIME_BASE_URL")
        ?? new UriBuilder(PublicBaseUrl) { Port = RealtimePort, Path = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Authority);

    public static LocalConfig FromEnvironment()
    {
        var publicBaseUrl = TrimEndSlash(Required("PUBLIC_BASE_URL"));
        var masterDataFile = ConfigPath("MASTER_DATA_FILE");
        var masterDataVersion = Required("MASTER_DATA_VERSION");
        var responseMode = Required("MSGPACK_RESPONSE_MODE");
        return new LocalConfig(
            Required("HOST"),
            ParsePort("PORT", Required("PORT")),
            publicBaseUrl,
            Required("APPLICATION_VERSION"),
            Required("ASSET_VERSION"),
            Required("MIN_APPLICATION_VERSION"),
            Required("MIN_ASSET_VERSION"),
            masterDataVersion,
            PublishTimestamp(),
            masterDataFile,
            Optional("MASTER_DATA_REMOTE_PATH") ?? BuildMasterDataRemotePath(masterDataFile, masterDataVersion),
            ConfigPath("RESOURCES_ROOT"),
            ConfigPath("ADDRESSABLES_ROOT"),
            TrimEndSlash(Optional("STATIC_CONTENT_URL") ?? $"{publicBaseUrl}/static"),
            Required("DATABASE_URL"),
            Required("TOKEN_SECRET"),
            responseMode,
            Optional("REGISTER_RESPONSE_MODE") ?? responseMode)
        {
            ResourcesBaseUrl = RemoteBaseUrl("RESOURCES_BASE_URL"),
            AddressablesBaseUrl = RemoteBaseUrl("ADDRESSABLES_BASE_URL")
        };
    }

    static string? RemoteBaseUrl(string key)
    {
        var value = Optional(key);
        if (value is null) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"配置 {key} 必须为不带凭据、查询参数或片段的 HTTP(S) 根地址");
        return uri.AbsoluteUri.TrimEnd('/');
    }

    static string? Optional(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    static string Required(string key) => Optional(key)
        ?? throw new InvalidOperationException($"缺少配置 {key}，请根据 .env.example 填写 .env 或设置环境变量");

    static string ConfigPath(string key) => Path.GetFullPath(Required(key),
        DotEnv.DirectoryPath ?? Directory.GetCurrentDirectory());

    static int ParsePort(string key, string value)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException($"配置 {key} 必须为 1–65535 的整数");
        return port;
    }

    static long PublishTimestamp()
    {
        if (!long.TryParse(Required("MASTER_DATA_PUBLISH_TIMESTAMP"), out var timestamp) || timestamp < 0)
            throw new InvalidOperationException("配置 MASTER_DATA_PUBLISH_TIMESTAMP 必须为非负整数");
        return timestamp;
    }

    static string TrimEndSlash(string value) => value.TrimEnd('/');

    static string BuildMasterDataRemotePath(string masterDataFile, string masterDataVersion)
    {
        var fileName = Path.GetFileName(masterDataFile);
        var parentName = Path.GetFileName(Path.GetDirectoryName(masterDataFile) ?? "");
        if (!string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(parentName))
        {
            return $"{parentName}/{fileName}";
        }

        return $"{masterDataVersion}.msgpack";
    }
}

