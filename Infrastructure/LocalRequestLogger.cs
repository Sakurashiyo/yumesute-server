sealed class LocalRequestLogger
{
    readonly string logPath;
    readonly bool writeToConsole;
    readonly SemaphoreSlim writeLock = new(1, 1);

    public LocalRequestLogger(string logPath, bool writeToConsole = true)
    {
        this.logPath = logPath;
        this.writeToConsole = writeToConsole;
    }

    public void Log(string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
        if (writeToConsole) Console.Write(line);
        writeLock.Wait();
        try
        {
            File.AppendAllText(logPath, line);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task LogAsync(string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
        if (writeToConsole) Console.Write(line);
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

