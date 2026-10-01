using System.IO.Compression;

sealed partial class LocalAssetResolver
{
    const int MaxScenePayloadBytes = 8 * 1024 * 1024;

    public async Task SendSceneFileAsync(HttpContext context, string? filePath, ApiCodec codec)
    {
        if (filePath is null || !File.Exists(filePath))
        {
            await SendLocalFileAsync(context, filePath, codec);
            return;
        }
        var payload = await ReadScenePayloadAsync(filePath, context.RequestAborted);
        codec.SetCommonHeaders(context);
        context.Response.ContentType = "application/octet-stream";
        context.Response.ContentLength = payload.Length;
        await logger.LogAsync($"scene-file operation=scene-read bytes={payload.Length} path={context.Request.Path} source={filePath} outcome=success");
        if (!HttpMethods.IsHead(context.Request.Method))
            await context.Response.Body.WriteAsync(payload, context.RequestAborted);
    }

    internal static async Task<byte[]> ReadScenePayloadAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(filePath);
        if (file.Length > MaxScenePayloadBytes) throw new InvalidDataException("故事文件超过大小上限");
        var signature = new byte[2];
        await file.ReadExactlyAsync(signature, cancellationToken);
        file.Position = 0;
        byte[] payload;
        if (signature[0] == 0x1f && signature[1] == 0x8b)
        {
            // 下载缓存保留了 HTTP gzip 外层；客户端剧情反序列化只接受内层 MessagePack/LZ4。
            await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: true);
            await using var output = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await gzip.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + count > MaxScenePayloadBytes)
                    throw new InvalidDataException("故事解压数据超过大小上限");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            payload = output.ToArray();
        }
        else
        {
            payload = new byte[checked((int)file.Length)];
            await file.ReadExactlyAsync(payload, cancellationToken);
        }
        // 在 HTTP 200 前验证源文件，避免把 HTML、损坏压缩包或错误资源交给客户端。
        if (MagicOnionLz4.Unwrap(MsgPack.Decode(payload), MaxScenePayloadBytes) is not object?[] { Length: > 0 } details
            || details.Any(detail => detail is not object?[]))
            throw new InvalidDataException("故事文件不是有效的剧情 MessagePack 数据");
        return payload;
    }
}
