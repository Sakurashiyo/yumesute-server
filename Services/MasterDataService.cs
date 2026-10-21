using Npgsql;

sealed class MasterDataService
{
    readonly LocalConfig config;
    readonly PostgresDatabase database;

    public MasterDataService(LocalConfig config, PostgresDatabase database)
    {
        this.config = config;
        this.database = database;
    }

    public async Task<MasterDataVersionInfo> GetActiveAsync()
    {
        var source = await GetSourceAsync();
        var operations = await new GameOperationsService(database).GetAsync();
        operations = operations with { Configuration = FilterResources(operations.Configuration) };
        return GachaMasterDataOverlay.Apply(source, operations);
    }

    GameOperationsConfiguration FilterResources(GameOperationsConfiguration configuration) => configuration with {
        Gachas = configuration.Gachas.ToDictionary(pair => pair.Key, pair => pair.Value
            && HistoricalGachaResources.Available(GachaRules.AllPools.Single(pool => pool.Id == pair.Key),config.ResourcesRoot))
    };

    async Task<MasterDataVersionInfo> GetSourceAsync()
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select version, publish_timestamp, remote_path, local_file_path, sas_token
            from system_master_data_versions
            where is_active
            order by updated_at desc
            limit 1
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return MasterDataVersionInfo.FromConfig(config);
        }

        return new MasterDataVersionInfo(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4));
    }
}

readonly record struct MasterDataVersionInfo(
    string Version,
    long PublishTimestamp,
    string RemotePath,
    string LocalFilePath,
    string SasToken)
{
    public static MasterDataVersionInfo FromConfig(LocalConfig config)
    {
        return new MasterDataVersionInfo(
            config.MasterDataVersion,
            config.MasterDataPublishTimestamp,
            config.MasterDataRemotePath,
            config.MasterDataFile,
            "");
    }
}
