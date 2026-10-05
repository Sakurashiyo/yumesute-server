using System.Text.Json;

static class PayloadReaders
{
    public static string? ReadApiToken(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization["Bearer ".Length..].Trim();
        }

        foreach (var header in new[] { "X-Api-Token", "Api-Token", "ApiToken" })
        {
            var value = context.Request.Headers[header].ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return null;
    }

    public static (int TabCategory, DateTime ReadAt) ReadNotificationReadPayload(object? payload)
    {
        object? category;
        object? timestamp;
        switch (payload)
        {
            case object?[] { Length: 2 } array:
                category = array[0]; timestamp = array[1]; break;
            case Dictionary<string, object?> map when map.TryGetValue("TabCategory", out category) && map.TryGetValue("ReadAt", out timestamp):
                break;
            case JsonElement json when json.ValueKind == JsonValueKind.Object
                && json.TryGetProperty("TabCategory", out var tab) && tab.ValueKind == JsonValueKind.Number && tab.TryGetInt32(out var number)
                && json.TryGetProperty("ReadAt", out var read) && read.ValueKind == JsonValueKind.String && read.TryGetDateTime(out var date):
                category = number; timestamp = date; break;
            case JsonElement json when json.ValueKind == JsonValueKind.Array && json.GetArrayLength() == 2
                && json[0].ValueKind == JsonValueKind.Number && json[0].TryGetInt32(out var number) && json[1].ValueKind == JsonValueKind.String && json[1].TryGetDateTime(out var date):
                category = number; timestamp = date; break;
            default:
                throw new BadHttpRequestException(NotificationErrors.InvalidReadRequest, 400);
        }
        if (category is not (byte or short or int or long) || Convert.ToInt64(category) is < 1 or > 3
            || timestamp is not DateTime readAt || readAt <= DateTime.UnixEpoch)
            throw new BadHttpRequestException(NotificationErrors.InvalidReadRequest, 400);
        // 只解析 ReadAt 字段，避免将前面的分类编号当成 Unix 时间；不允许客户端时钟提前标记未来公告。
        var utc = readAt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(readAt, DateTimeKind.Utc) : readAt.ToUniversalTime();
        var now = DateTime.UtcNow;
        return ((int)Convert.ToInt64(category), utc > now ? now : utc);
    }

    public static string? ReadRegisterName(object? value)
    {
        return value switch
        {
            object?[] array when array.Length > 0 => array[0]?.ToString(),
            Dictionary<string, object?> map when map.TryGetValue("Name", out var name) => name?.ToString(),
            JsonElement json when json.ValueKind == JsonValueKind.Object && json.TryGetProperty("Name", out var name) => name.GetString(),
            JsonElement json when json.ValueKind == JsonValueKind.Array && json.GetArrayLength() > 0 => json[0].GetString(),
            _ => null
        };
    }

    public static string? ReadLoginToken(object? value)
    {
        return value switch
        {
            object?[] array when array.Length > 0 => array[0]?.ToString(),
            Dictionary<string, object?> map when map.TryGetValue("LoginToken", out var token) => token?.ToString(),
            JsonElement json when json.ValueKind == JsonValueKind.Object && json.TryGetProperty("LoginToken", out var token) => token.GetString(),
            JsonElement json when json.ValueKind == JsonValueKind.Array && json.GetArrayLength() > 0 => json[0].GetString(),
            _ => null
        };
    }

    public static AuthenticatePayload ReadAuthenticatePayload(object? value, string defaultApplicationVersion)
    {
        if (value is object?[] array)
        {
            return new AuthenticatePayload(
                array.ElementAtOrDefault(0)?.ToString() ?? "",
                array.ElementAtOrDefault(1)?.ToString() ?? "Unknown",
                array.ElementAtOrDefault(2)?.ToString() ?? "",
                array.ElementAtOrDefault(3)?.ToString() ?? "",
                array.ElementAtOrDefault(4)?.ToString() ?? defaultApplicationVersion);
        }

        if (value is Dictionary<string, object?> map)
        {
            return new AuthenticatePayload(
                MapText(map, "LoginToken") ?? "",
                MapText(map, "GameVersion") ?? "Unknown",
                MapText(map, "ApkHash") ?? "",
                MapText(map, "ApkApplicationSignature") ?? "",
                MapText(map, "ApplicationVersion") ?? defaultApplicationVersion);
        }

        if (value is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            return new AuthenticatePayload(
                JsonText(json, "LoginToken") ?? "",
                JsonText(json, "GameVersion") ?? "Unknown",
                JsonText(json, "ApkHash") ?? "",
                JsonText(json, "ApkApplicationSignature") ?? "",
                JsonText(json, "ApplicationVersion") ?? defaultApplicationVersion);
        }

        return new AuthenticatePayload("", "Unknown", "", "", defaultApplicationVersion);
    }

    public static AuthenticatePayload ReadAuthenticatePayloadFromRaw(byte[] bytes, string defaultApplicationVersion)
    {
        var frames = MagicOnionLz4.UnwrapFrames(MsgPack.DecodeAll(bytes));
        var payload = FindMagicOnionPayload(frames);
        if (payload is not null)
        {
            return ReadAuthenticatePayload(payload, defaultApplicationVersion);
        }

        var payloadFrame = frames
            .OfType<object?[]>()
            .LastOrDefault(IsAuthenticatePayloadArray);
        if (payloadFrame is not null)
        {
            return ReadAuthenticatePayload(payloadFrame, defaultApplicationVersion);
        }

        if (frames.Length == 1)
        {
            return ReadAuthenticatePayload(frames[0], defaultApplicationVersion);
        }

        if (frames.Length >= 2 && frames[0] is null)
        {
            return ReadAuthenticatePayload(frames[1], defaultApplicationVersion);
        }

        if (frames.Length >= 5)
        {
            return new AuthenticatePayload(
                frames[0]?.ToString() ?? "",
                frames[1]?.ToString() ?? "Unknown",
                frames[2]?.ToString() ?? "",
                frames[3]?.ToString() ?? "",
                frames[4]?.ToString() ?? defaultApplicationVersion);
        }

        return ReadAuthenticatePayload(frames.ElementAtOrDefault(0), defaultApplicationVersion);
    }

    static object?[]? FindMagicOnionPayload(object?[] frames)
    {
        if (frames.Length == 1 && frames[0] is object?[] envelope)
        {
            return FindMagicOnionPayload(envelope);
        }

        if (frames.Length >= 2 && frames[1] is object?[] payload && IsAuthenticatePayloadArray(payload))
        {
            return payload;
        }

        return frames
            .OfType<object?[]>()
            .Select(FindMagicOnionPayload)
            .FirstOrDefault(payload => payload is not null);
    }

    static bool IsAuthenticatePayloadArray(object?[] array)
    {
        if (array.Length < 5) return false;
        return array[0] is string || array[4] is string;
    }

    static string? MapText(Dictionary<string, object?> map, string key)
    {
        return map.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    static string? JsonText(JsonElement json, string key)
    {
        if (!json.TryGetProperty(key, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}

