static class DotEnv
{
    public static string? DirectoryPath { get; private set; }

    public static void Load(string path)
    {
        if (!File.Exists(path)) return;
        // 相对资源路径以实际加载的配置文件目录为基准，避免启动目录变化造成资源丢失。
        DirectoryPath = Path.GetDirectoryName(Path.GetFullPath(path));

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && IsQuoted(value))
            {
                value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    static bool IsQuoted(string value)
    {
        return value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'';
    }
}

