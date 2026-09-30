sealed class LocalRequestLogger
{
    readonly string logPath;
    readonly SemaphoreSlim writeLock = new(1, 1);

    public LocalRequestLogger(string logPath)
    {
        this.logPath = logPath;
    }

    public async Task LogAsync(string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
        Console.Write(line);
        await writeLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(logPath, line);
        }
        finally
        {
            writeLock.Release();
        }
    }
}

