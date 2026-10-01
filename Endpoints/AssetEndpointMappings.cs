static class AssetEndpointMappings
{
    public static void MapAssetEndpoints(this WebApplication app, LocalServerState state)
    {
        var config = state.Config;
        var codec = state.Codec;
        var assets = state.Assets;

        app.MapMethods("/master/{**file}", new[] { "GET", "HEAD" }, async (HttpContext context, string? file) =>
        {
            codec.SetCommonHeaders(context);
            var normalizedFile = Uri.UnescapeDataString(file ?? "").Replace('\\', '/').TrimStart('/');
            if (normalizedFile.StartsWith("scenes/", StringComparison.OrdinalIgnoreCase))
            {
                await assets.SendSceneFileAsync(context, assets.ResolveAssetFile(normalizedFile), codec);
                return;
            }

            if (TryReadEpisodeDetailAssetSource(normalizedFile, out var episodeMasterId))
            {
                await SendEpisodeDetailAssetSourceAsync(context, codec, state.Logger, episodeMasterId, $"master file={file}");
                return;
            }

            var masterData = await state.MasterDataService.GetActiveAsync();
            if (!File.Exists(masterData.LocalFilePath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("master data file is not configured");
                return;
            }

            context.Response.ContentType = "application/octet-stream";
            await context.Response.SendFileAsync(masterData.LocalFilePath);
        });

        app.MapMethods("/scenes/{file}", new[] { "GET", "HEAD" }, async (HttpContext context, string file) =>
        {
            await assets.SendSceneFileAsync(context, assets.ResolveAssetFile($"scenes/{file}"), codec);
        });

        app.MapMethods("/localassets/{**path}", new[] { "GET", "HEAD" }, async (HttpContext context, string? path) =>
        {
            await assets.SendLocalFileAsync(context, assets.ResolveAssetFile(path ?? ""), codec);
        });

        app.MapMethods("/Resources/{**path}", new[] { "GET", "HEAD" }, async (HttpContext context, string? path) =>
        {
            await assets.SendLocalFileAsync(context, assets.ResolveAssetFile(path ?? ""), codec);
        });

        app.MapMethods("/static/{**path}", new[] { "GET", "HEAD" }, async (HttpContext context, string? path) =>
        {
            if (ShouldServeTransparentPng(path))
            {
                await SendTransparentPngAsync(context, codec, state.Logger);
                return;
            }

            var file = assets.ResolveAssetFile(path ?? "");
            var normalized = Uri.UnescapeDataString(path ?? "").Replace('\\', '/').TrimStart('/');
            if (file is null && normalized.StartsWith("Resources/Textures/Banners/", StringComparison.OrdinalIgnoreCase)
                && normalized.EndsWith(".astc.gz", StringComparison.OrdinalIgnoreCase))
            {
                await SendTransparentAstcAsync(context, codec, state.Logger);
                return;
            }
            await assets.SendLocalFileAsync(context, file, codec);
        });

        app.MapMethods("/com.unity.addressables/{**path}", new[] { "GET", "HEAD" }, async (HttpContext context, string? path) =>
        {
            await assets.SendLocalFileAsync(context, assets.ResolveAddressablesFile(path ?? ""), codec);
        });

        app.MapMethods("/localassets/com.unity.addressables/{**path}", new[] { "GET", "HEAD" }, async (HttpContext context, string? path) =>
        {
            await assets.SendLocalFileAsync(context, assets.ResolveAddressablesFile(path ?? ""), codec);
        });
    }

    static bool TryReadEpisodeDetailAssetSource(string file, out long episodeMasterId)
    {
        episodeMasterId = 0;
        const string marker = "/Episodes/";
        var normalized = "/" + Uri.UnescapeDataString(file).Replace('\\', '/');
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || !normalized.EndsWith("/DetailAssetSource", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var start = index + marker.Length;
        var end = normalized.IndexOf('/', start);
        return end > start && long.TryParse(normalized[start..end], out episodeMasterId);
    }

    static bool TryReadSceneEpisodeId(string file, out long episodeMasterId)
    {
        episodeMasterId = 0;
        var fileName = Path.GetFileName(Uri.UnescapeDataString(file).Replace('\\', '/'));
        if (!fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) return false;

        var idPart = fileName[..^".bin".Length];
        var separatorIndex = idPart.IndexOf('_');
        if (separatorIndex > 0)
        {
            idPart = idPart[..separatorIndex];
        }

        return long.TryParse(idPart, out episodeMasterId);
    }

    static async Task SendEpisodeDetailAssetSourceAsync(
        HttpContext context,
        ApiCodec codec,
        LocalRequestLogger logger,
        long episodeMasterId,
        string source)
    {
        codec.SetCommonHeaders(context);
        context.Response.ContentType = "application/vnd.msgpack";
        var result = TutorialEpisodeResults.Details(episodeMasterId);
        var payload = MsgPack.Encode(result);
        context.Response.Headers.ContentLength = payload.Length;
        await logger.LogAsync($"episode-detail-asset-source episodeMasterId={episodeMasterId} {source} bytes={payload.Length} result={ValueFormatter.Format(result)}");

        if (HttpMethods.IsHead(context.Request.Method)) return;
        await context.Response.Body.WriteAsync(payload);
    }

    static bool ShouldServeTransparentPng(string? path)
    {
        var normalized = Uri.UnescapeDataString(path ?? "").Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized)) return true;

        var fileName = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(fileName) ||
            !Path.HasExtension(fileName) &&
            normalized.StartsWith("Resources/Textures/Banners", StringComparison.OrdinalIgnoreCase);
    }

    static async Task SendTransparentAstcAsync(HttpContext context, ApiCodec codec, LocalRequestLogger logger)
    {
        // 客户端图片下载失败时不会释放并发名额；缺失横幅须使用格式正确的透明占位。
        // 真实横幅在路由中优先返回，此占位只用于尚未下载的静态横幅。
        var astc = Convert.FromHexString("13ABA15C060601060000060000010000FCFDFFFFFFFFFFFF0000000000000000");
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            await gzip.WriteAsync(astc);
        var bytes = output.ToArray();
        codec.SetCommonHeaders(context);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/octet-stream";
        context.Response.ContentLength = bytes.Length;
        await logger.LogAsync($"WARN static banner missing: transparent-astc placeholder bytes={bytes.Length} path={context.Request.Path}");
        if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.Body.WriteAsync(bytes);
    }

    static async Task SendTransparentPngAsync(HttpContext context, ApiCodec codec, LocalRequestLogger logger)
    {
        codec.SetCommonHeaders(context);
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGBgAAAABQABpfZFQAAAAABJRU5ErkJggg==");
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "image/png";
        context.Response.Headers.ContentLength = bytes.Length;
        await logger.LogAsync($"static transparent-png bytes={bytes.Length} path={context.Request.Path}");

        if (HttpMethods.IsHead(context.Request.Method)) return;
        await context.Response.Body.WriteAsync(bytes);
    }
}

