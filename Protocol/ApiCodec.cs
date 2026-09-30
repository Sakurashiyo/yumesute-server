using System.Text.Json;

sealed class ApiCodec
{
    readonly LocalConfig config;
    readonly LocalRequestLogger logger;

    public ApiCodec(LocalConfig config, LocalRequestLogger logger)
    {
        this.config = config;
        this.logger = logger;
    }

    public async Task<object?> ReadRequestBodyAsync(HttpContext context)
    {
        var bytes = await ReadRequestBytesAsync(context);
        if (bytes.Length == 0) return null;

        return await DecodeAndLogRequestBodyAsync(context, bytes);
    }

    public async Task<byte[]> ReadRequestBytesAsync(HttpContext context)
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        var bytes = ms.ToArray();
        await logger.LogAsync($"body length={bytes.Length} path={context.Request.Path}");
        if (bytes.Length > 0)
        {
            await logger.LogAsync($"body raw path={context.Request.Path} hex={Bytes.ToHex(bytes, 256)}");
        }
        return bytes;
    }

    public async Task<object?> DecodeAndLogRequestBodyAsync(HttpContext context, byte[] bytes)
    {
        var contentType = context.Request.ContentType ?? "";
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var json = JsonSerializer.Deserialize<object>(bytes);
            await logger.LogAsync($"body decoded path={context.Request.Path} value={ValueFormatter.Format(json)}");
            return json;
        }

        var decoded = MagicOnionLz4.Unwrap(MsgPack.Decode(bytes));
        await logger.LogAsync($"body decoded path={context.Request.Path} value={ValueFormatter.Format(decoded)}");
        return decoded;
    }

    public async Task WriteApiResultAsync(HttpContext context, object? result, string? responseMode = null)
    {
        SetCommonHeaders(context);
        context.Response.ContentType = "application/vnd.msgpack";
        var mode = responseMode ?? config.MsgPackResponseMode;
        var payload = mode switch
        {
            "direct" => MsgPack.Encode(result),
            "two-frame" => Bytes.Concat(MsgPack.Encode(Array.Empty<object>()), MsgPack.Encode(result)),
            "nil-two-frame" => Bytes.Concat(MsgPack.Encode(null), MsgPack.Encode(result)),
            "result-two-frame" => Bytes.Concat(MsgPack.Encode(result), MsgPack.Encode(Array.Empty<object>())),
            "five-frame" => Bytes.Concat(MsgPack.Encode(Array.Empty<object>()), MsgPack.Encode(result), MsgPack.Encode(Array.Empty<object>()), MsgPack.Encode(Array.Empty<object>()), MsgPack.Encode(Array.Empty<object>())),
            "lz4-five-frame" => Bytes.Concat(MsgPack.Encode(Array.Empty<object>()), MsgPack.Encode(MagicOnionLz4.Wrap(result)), MsgPack.Encode(Array.Empty<object>()), MsgPack.Encode(Array.Empty<object>()), MsgPack.Encode(Array.Empty<object>())),
            "envelope" => MsgPack.Encode(new Dictionary<string, object?>
            {
                ["errors"] = Array.Empty<object>(),
                ["result"] = result,
                ["present"] = Array.Empty<object>(),
                ["deleted"] = Array.Empty<object>(),
                ["notifications"] = Array.Empty<object>()
            }),
            _ => Bytes.Concat(MsgPack.Encode(Array.Empty<object>()), MsgPack.Encode(result))
        };

        context.Response.Headers.ContentLength = payload.Length;
        await logger.LogAsync($"response mode={mode} bytes={payload.Length} prefix={Bytes.ToHex(payload, 48)} path={context.Request.Path} result={ValueFormatter.Format(result)}");
        await context.Response.Body.WriteAsync(payload);
    }

    public async Task WriteApiFramesAsync(
        HttpContext context,
        object? result,
        object? present,
        object? deleted,
        object? notifications,
        string responseMode = "five-frame")
    {
        SetCommonHeaders(context);
        context.Response.ContentType = "application/vnd.msgpack";
        var payload = responseMode switch
        {
            "login-official-five-frame" => Bytes.Concat(
                MsgPack.Encode(Array.Empty<object>()),
                MsgPack.Encode(result),
                MsgPack.Encode(MagicOnionLz4.Wrap(present ?? Array.Empty<object>())),
                MsgPack.Encode(deleted ?? Array.Empty<object>()),
                MsgPack.Encode(notifications ?? Array.Empty<object>())),
            "lz4-five-frame" => Bytes.Concat(
                MsgPack.Encode(Array.Empty<object>()),
                MsgPack.Encode(MagicOnionLz4.Wrap(result)),
                MsgPack.Encode(MagicOnionLz4.Wrap(present ?? Array.Empty<object>())),
                MsgPack.Encode(deleted ?? Array.Empty<object>()),
                MsgPack.Encode(notifications ?? Array.Empty<object>())),
            "result-lz4-five-frame" => Bytes.Concat(
                MsgPack.Encode(Array.Empty<object>()),
                MsgPack.Encode(MagicOnionLz4.Wrap(result)),
                MsgPack.Encode(present ?? Array.Empty<object>()),
                MsgPack.Encode(deleted ?? Array.Empty<object>()),
                MsgPack.Encode(notifications ?? Array.Empty<object>())),
            "result-present-lz4-five-frame" => Bytes.Concat(
                MsgPack.Encode(Array.Empty<object>()),
                MsgPack.Encode(MagicOnionLz4.Wrap(result)),
                MsgPack.Encode(MagicOnionLz4.Wrap(present ?? Array.Empty<object>())),
                MsgPack.Encode(deleted ?? Array.Empty<object>()),
                MsgPack.Encode(notifications ?? Array.Empty<object>())),
            "result-present-lz4-when-not-empty-five-frame" => Bytes.Concat(
                MsgPack.Encode(Array.Empty<object>()),
                IsEmptyArray(result)
                    ? MsgPack.Encode(Array.Empty<object>())
                    : MsgPack.Encode(MagicOnionLz4.Wrap(result)),
                IsEmptyArray(present)
                    ? MsgPack.Encode(Array.Empty<object>())
                    : MsgPack.Encode(MagicOnionLz4.Wrap(present ?? Array.Empty<object>())),
                MsgPack.Encode(deleted ?? Array.Empty<object>()),
                MsgPack.Encode(notifications ?? Array.Empty<object>())),
            "present-lz4-when-not-empty-five-frame" => Bytes.Concat(
                MsgPack.Encode(Array.Empty<object>()),
                MsgPack.Encode(result),
                IsEmptyArray(present)
                    ? MsgPack.Encode(Array.Empty<object>())
                    : MsgPack.Encode(MagicOnionLz4.Wrap(present ?? Array.Empty<object>())),
                MsgPack.Encode(deleted ?? Array.Empty<object>()),
                MsgPack.Encode(notifications ?? Array.Empty<object>())),
            _ => Bytes.Concat(
                MsgPack.Encode(Array.Empty<object>()),
                MsgPack.Encode(result),
                MsgPack.Encode(present ?? Array.Empty<object>()),
                MsgPack.Encode(deleted ?? Array.Empty<object>()),
                MsgPack.Encode(notifications ?? Array.Empty<object>()))
        };

        context.Response.Headers.ContentLength = payload.Length;
        await logger.LogAsync($"response mode={responseMode} bytes={payload.Length} prefix={Bytes.ToHex(payload, 48)} path={context.Request.Path} result={ValueFormatter.Format(result)} present={ValueFormatter.Format(present)}");
        await context.Response.Body.WriteAsync(payload);
    }

    static bool IsEmptyArray(object? value)
    {
        return value switch
        {
            null => true,
            Array array => array.Length == 0,
            System.Collections.ICollection collection => collection.Count == 0,
            _ => false
        };
    }

    public void SetCommonHeaders(HttpContext context)
    {
        context.Response.Headers["X-Client-Version"] = config.MinApplicationVersion;
        context.Response.Headers["X-Assets-Version"] = config.MinAssetVersion;
        context.Response.Headers["X-Server-Version"] = config.ApplicationVersion;
        context.Response.Headers["X-MasterData-Version"] = config.MasterDataVersion;
        context.Response.Headers["X-MasterData-Uri"] = $"{config.PublicBaseUrl}/master/{config.MasterDataRemotePath}";
        context.Response.Headers["X-MasterData-SasToken"] = "";
        context.Response.Headers["X-MasterData-PublishTimestamp"] = config.MasterDataPublishTimestamp.ToString();
        context.Response.Headers["X-FM"] = "0";
        context.Response.Headers["X-Maintenance"] = "false";
        context.Response.Headers["X-Maintenance-Message"] = "";
        context.Response.Headers["X-Token-Expired"] = "false";
    }
}

