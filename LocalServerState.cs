using System.Collections.Concurrent;

sealed class LocalServerState
{
    public LocalServerState(LocalConfig config)
    {
        Config = config;
        Logger = new LocalRequestLogger(Path.Combine(AppContext.BaseDirectory, "requests.log"));
        lastRegisteredLoginTokenPath = Path.Combine(AppContext.BaseDirectory, "last-login-token.txt");
        LastRegisteredLoginToken = LoadLastRegisteredLoginToken();
        Database = new PostgresDatabase(config.DatabaseUrl);
        AccountService = new AccountService(config, Database);
        UserDataService = new UserDataService(Database);
        MasterDataService = new MasterDataService(config, Database);
        MultiLiveRealtime = new MultiLiveRealtimeService(Logger);
        Codec = new ApiCodec(config, Logger);
        Assets = new LocalAssetResolver(config, Logger);
    }

    public LocalConfig Config { get; }
    public ConcurrentDictionary<string, LocalAccount> Accounts { get; } = new();
    public string? LastRegisteredLoginToken { get; set; }
    readonly string lastRegisteredLoginTokenPath;
    public PostgresDatabase Database { get; }
    public AccountService AccountService { get; }
    public UserDataService UserDataService { get; }
    public MasterDataService MasterDataService { get; }
    public MultiLiveRealtimeService MultiLiveRealtime { get; }
    public LocalRequestLogger Logger { get; }
    public ApiCodec Codec { get; }
    public LocalAssetResolver Assets { get; }

    public async Task SaveLastRegisteredLoginTokenAsync(string loginToken)
    {
        LastRegisteredLoginToken = loginToken;
        await File.WriteAllTextAsync(lastRegisteredLoginTokenPath, loginToken);
    }

    string? LoadLastRegisteredLoginToken()
    {
        if (!File.Exists(lastRegisteredLoginTokenPath)) return null;
        var token = File.ReadAllText(lastRegisteredLoginTokenPath).Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}

