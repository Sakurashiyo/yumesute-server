using System.Collections.Concurrent;

sealed partial class LocalAssetResolver
{
    readonly LocalConfig config;
    readonly LocalRequestLogger logger;
    public LocalAssetResolver(LocalConfig config, LocalRequestLogger logger)
    {
        this.config = config;
        this.logger = logger;
    }

    public string? ResolveAssetFile(string requestPath)
    {
        var candidates = BuildPathCandidates(requestPath);
        foreach (var candidate in candidates)
        {
            var direct = SafeCombine(config.ResourcesRoot, candidate);
            if (direct is not null && File.Exists(direct)) return direct;

            var addressables = SafeCombine(config.AddressablesRoot, candidate);
            if (addressables is not null && File.Exists(addressables)) return addressables;
        }

        var comicImage = ResolveComicImageFile(requestPath);
        if (comicImage is not null) return comicImage;

        var versionedScene = ResolveVersionedSceneFile(requestPath);
        if (versionedScene is not null) return versionedScene;

        var catalog = ResolveAddressablesCatalog(requestPath);
        if (catalog is not null) return catalog;

        // 谱面包含歌曲专属的音频 cue，绝不能按同名文件回退到另一首歌。
        if (candidates.Any(candidate => candidate.StartsWith("Notations/", StringComparison.OrdinalIgnoreCase)))
            return null;

        return FindByFileName(Path.GetFileName(requestPath.Replace('\\', '/')));
    }

    string? ResolveComicImageFile(string requestPath)
    {
        var decoded = RequestPath.Normalize(requestPath);
        if (decoded is null) return null;
        var normalized = decoded.TrimStart('/');
        var comicPath = normalized.StartsWith("Resources/comic/", StringComparison.OrdinalIgnoreCase)
            ? normalized["Resources/".Length..]
            : normalized;
        if (!comicPath.StartsWith("comic/", StringComparison.OrdinalIgnoreCase) ||
            Path.HasExtension(comicPath))
        {
            return null;
        }

        var imagePath = $"{comicPath}.png";
        return SafeCombine(config.ResourcesRoot, imagePath) is { } resourcePath && File.Exists(resourcePath)
            ? resourcePath
            : SafeCombine(config.AddressablesRoot, imagePath) is { } addressablesPath && File.Exists(addressablesPath)
                ? addressablesPath
                : null;
    }

    string? ResolveVersionedSceneFile(string requestPath)
    {
        var decoded = RequestPath.Normalize(requestPath);
        if (decoded is null) return null;
        var normalized = decoded.TrimStart('/');
        if (!normalized.StartsWith("scenes/", StringComparison.OrdinalIgnoreCase)) return null;

        var requestedName = Path.GetFileName(normalized);
        if (!requestedName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) return null;
        var logicalName = Path.GetFileNameWithoutExtension(requestedName);
        if (logicalName.Length == 0 || !logicalName.All(char.IsDigit)) return null;

        foreach (var candidate in BuildPathCandidates(normalized))
        {
            var relativeDirectory = Path.GetDirectoryName(candidate.Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(relativeDirectory)) continue;
            var directory = SafeCombine(config.ResourcesRoot, relativeDirectory);
            if (directory is null || !Directory.Exists(directory)) continue;

            var matches = Directory.EnumerateFiles(directory, $"{logicalName}_*.bin", SearchOption.TopDirectoryOnly)
                .Where(path => IsVersionedSceneFile(path, logicalName))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
            if (matches.Length == 1) return matches[0];
        }

        return null;
    }

    static bool IsVersionedSceneFile(string path, string logicalName)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var suffix = name[(logicalName.Length + 1)..];
        return suffix.Length > 0 && suffix.All(character => char.IsAsciiHexDigit(character));
    }

    public string? ResolveAddressablesFile(string requestPath)
    {
        foreach (var candidate in BuildPathCandidates(requestPath))
        {
            var direct = SafeCombine(config.AddressablesRoot, candidate);
            if (direct is not null && File.Exists(direct)) return direct;

            var resource = SafeCombine(config.ResourcesRoot, candidate);
            if (resource is not null && File.Exists(resource)) return resource;
        }

        var catalog = ResolveAddressablesCatalog(requestPath);
        if (catalog is not null) return catalog;

        return FindByFileName(Path.GetFileName(requestPath.Replace('\\', '/')));
    }

    public async Task SendLocalFileAsync(HttpContext context, string? filePath, ApiCodec codec)
    {
        codec.SetCommonHeaders(context);
        if (filePath is null || !File.Exists(filePath))
        {
            await logger.LogAsync($"file miss path={context.Request.Path}");
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = ContentTypes.Guess(filePath);

        if (context.Request.Path.Value?.EndsWith(".br", StringComparison.OrdinalIgnoreCase) == true &&
            !filePath.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "application/octet-stream";
            await using var compressed = new MemoryStream();
            await using (var input = File.OpenRead(filePath))
            await using (var brotli = new System.IO.Compression.BrotliStream(compressed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            {
                await input.CopyToAsync(brotli);
            }

            context.Response.ContentLength = compressed.Length;
            await logger.LogAsync($"file hit brotli-raw bytes={compressed.Length} path={context.Request.Path} -> {filePath}");
            if (HttpMethods.IsHead(context.Request.Method)) return;

            compressed.Position = 0;
            await compressed.CopyToAsync(context.Response.Body);
            return;
        }

        context.Response.Headers.ContentLength = new FileInfo(filePath).Length;
        await logger.LogAsync($"file hit bytes={context.Response.Headers.ContentLength} path={context.Request.Path} -> {filePath}");

        if (HttpMethods.IsHead(context.Request.Method)) return;
        await context.Response.SendFileAsync(filePath);
    }

    string? ResolveAddressablesCatalog(string requestPath)
    {
        var decoded = RequestPath.Normalize(requestPath);
        if (decoded is null) return null;
        var normalized = decoded.TrimStart('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var assetGroup = parts.FirstOrDefault(part =>
            part.Equals("2d-assets", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("3d-assets", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("cri-assets", StringComparison.OrdinalIgnoreCase));

        if (assetGroup is null ||
            !Path.GetFileName(normalized).StartsWith("catalog_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var requestedName = Path.GetFileName(normalized);
        var catalogExtension = requestedName.EndsWith(".hash", StringComparison.OrdinalIgnoreCase)
            ? ".hash"
            : ".json";
        var catalogName = $"catalog_{assetGroup}{catalogExtension}";
        var direct = SafeCombine(config.AddressablesRoot, $"{assetGroup}/{catalogName}");
        if (direct is not null && File.Exists(direct)) return direct;

        return Directory.EnumerateFiles(config.AddressablesRoot, catalogName, SearchOption.AllDirectories).FirstOrDefault();
    }

    static IEnumerable<string> BuildPathCandidates(string requestPath)
    {
        var decoded = RequestPath.Normalize(requestPath);
        if (decoded is null) yield break;
        var normalized = decoded.TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)) yield break;

        yield return normalized;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Equals("Android", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("Resources", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("localassets", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("com.unity.addressables", StringComparison.OrdinalIgnoreCase) ||
                LooksLikeVersion(parts[i]))
            {
                var tail = string.Join('/', parts.Skip(i + 1));
                if (!string.IsNullOrWhiteSpace(tail)) yield return tail;
            }
        }
    }

    static string? SafeCombine(string root, string relativePath)
    {
        if (relativePath.Contains('\0') || Path.IsPathRooted(relativePath)) return null;
        // 根目录必须以分隔符结尾，否则 "Resources-evil" 也会通过 "Resources" 前缀检查。
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    // 按文件名全盘回退时禁止通配符，否则 "*" 会匹配任意文件。
    static bool IsPlainFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && fileName.IndexOfAny(['*', '?']) < 0 && fileName is not "." and not ".." &&
        FallbackExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    // 只允许常见资源扩展名进入同名回退，避免把任意配置/日志文件也拍平暴露。
    static readonly HashSet<string> FallbackExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".astc", ".gz", ".br", ".bundle", ".json", ".bin", ".txt", ".bytes",
        ".mp3", ".wav", ".acb", ".awb", ".ogg", ".mp4", ".usm", ".anim", ".mask", ".hash", ".dat", ".csv"
    };

    // 同名回退需要整树遍历，必须缓存结果（含未命中），否则每次 miss 都会扫全盘。
    readonly ConcurrentDictionary<string, string?> fallbackCache = new(StringComparer.OrdinalIgnoreCase);
    const int FallbackCacheLimit = 4096;

    string? FindByFileName(string fileName)
    {
        if (!IsPlainFileName(fileName)) return null;
        if (fallbackCache.TryGetValue(fileName, out var cached)) return cached;
        var found = Directory.EnumerateFiles(config.ResourcesRoot, fileName, SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(config.AddressablesRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
        // 缓存上限只是防止长时间运行的进程无限增长；超限后停止新增，未命中仍然返回 null。
        if (fallbackCache.Count < FallbackCacheLimit) fallbackCache[fileName] = found;
        return found;
    }

    static bool LooksLikeVersion(string value)
    {
        return value.Count(ch => ch == '.') >= 1 && value.All(ch => char.IsDigit(ch) || ch == '.');
    }
}

