sealed class LocalServerState
{
    public LocalServerState(LocalConfig config)
    {
        Config = config;
        Logger = new LocalRequestLogger(Path.Combine(AppContext.BaseDirectory, "requests.log"));
        Database = new PostgresDatabase(config.DatabaseUrl);
        AccountService = new AccountService(config, Database);
        UserDataService = new UserDataService(Database);
        MasterDataService = new MasterDataService(config, Database);
        MultiLiveRealtime = new MultiLiveRealtimeService(Logger);
        Codec = new ApiCodec(config, Logger);
        Assets = new LocalAssetResolver(config, Logger);
    }

    public LocalConfig Config { get; }
    public PostgresDatabase Database { get; }
    public AccountService AccountService { get; }
    public UserDataService UserDataService { get; }
    public MasterDataService MasterDataService { get; }
    public MultiLiveRealtimeService MultiLiveRealtime { get; }
    public LocalRequestLogger Logger { get; }
    public ApiCodec Codec { get; }
    public LocalAssetResolver Assets { get; }

}
