using System.Text.Json;

static class AccountRecoveryPayloads
{
    public static async Task<string[]> ReadAsync(HttpContext context, ApiCodec codec, params string[] names)
    {
        var bytes = await codec.ReadRequestBytesAsync(context);
        object? payload;
        try
        {
            if (context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            {
                using var json = JsonDocument.Parse(bytes);
                payload = json.RootElement.Clone();
            }
            else
            {
                var frames = MagicOnionLz4.UnwrapFrames(MsgPack.DecodeAll(bytes));
                payload = frames.Length switch
                {
                    1 => frames[0],
                    2 when frames[0] is null or object?[] { Length: 0 } => frames[1],
                    _ => throw new BadHttpRequestException(AccountErrors.InvalidRecoveryRequest, 400)
                };
                if (payload is object?[] { Length: 2 } envelope && envelope[0] is null or object?[] { Length: 0 }) payload = envelope[1];
            }
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or JsonException
            or IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
        { throw new BadHttpRequestException(AccountErrors.InvalidRecoveryRequest, 400); }
        var result = new string[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var value = payload switch
            {
                object?[] array when array.Length == names.Length => array[i] as string,
                Dictionary<string, object?> map when map.Count == names.Length && map.TryGetValue(names[i], out var entry) => entry as string,
                JsonElement json when json.ValueKind == JsonValueKind.Array && json.GetArrayLength() == names.Length
                    && json[i].ValueKind == JsonValueKind.String => json[i].GetString(),
                JsonElement json when json.ValueKind == JsonValueKind.Object && json.TryGetProperty(names[i], out var entry)
                    && entry.ValueKind == JsonValueKind.String => entry.GetString(),
                _ => null
            };
            result[i] = value ?? throw new BadHttpRequestException(AccountErrors.InvalidRecoveryRequest, 400);
        }
        return result;
    }
}
