sealed class LocalServerState
{
    public LocalServerState(LocalConfig config)
    {
        Config = config;
        Logger = new LocalRequestLogger(Path.Combine(AppContext.BaseDirectory, "requests.log"));
        RealtimeLogger = new LocalRequestLogger(Path.Combine(AppContext.BaseDirectory, "realtime.log"), writeToConsole: false);
        Database = new PostgresDatabase(config.DatabaseUrl);
        GameOperations = new GameOperationsService(Database);
        AccountService = new AccountService(config, Database);
        UserDataService = new UserDataService(Database, config);
        MasterDataService = new MasterDataService(config, Database);
        MultiLiveRealtime = new MultiLiveRealtimeService(RealtimeLogger);
        SocialRealtime = new SocialRealtimeService(Database, MultiLiveRealtime);
        UserDataService.FriendRequestCreated = SocialRealtime.NotifyFriendRequestAsync;
        Codec = new ApiCodec(config, Logger);
        Assets = new LocalAssetResolver(config, Logger);
    }

    public LocalConfig Config { get; }
    public PostgresDatabase Database { get; }
    public GameOperationsService GameOperations { get; }
    public AccountService AccountService { get; }
    public UserDataService UserDataService { get; }
    public MasterDataService MasterDataService { get; }
    public MultiLiveRealtimeService MultiLiveRealtime { get; }
    public LocalRequestLogger Logger { get; }
    public LocalRequestLogger RealtimeLogger { get; }
    public SocialRealtimeService SocialRealtime { get; }
    public ApiCodec Codec { get; }
    public LocalAssetResolver Assets { get; }

}
