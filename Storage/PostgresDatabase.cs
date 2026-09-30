using Npgsql;

sealed class PostgresDatabase : IAsyncDisposable
{
    readonly NpgsqlDataSource dataSource;

    public PostgresDatabase(string connectionString)
    {
        dataSource = NpgsqlDataSource.Create(ToNpgsqlConnectionString(connectionString));
    }

    public NpgsqlConnection OpenConnection()
    {
        return dataSource.OpenConnection();
    }

    public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        return dataSource.OpenConnectionAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return dataSource.DisposeAsync();
    }

    static string ToNpgsqlConnectionString(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.Scheme.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? ""),
            Password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? "")
        };

        return builder.ConnectionString;
    }
}

