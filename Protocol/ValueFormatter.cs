using System.Text.Json;

static class ValueFormatter
{
    public static string Format(object? value, int depth = 0)
    {
        if (depth > 4) return "...";
        return value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            bool boolean => boolean ? "true" : "false",
            byte[] bytes => $"bytes[{bytes.Length}]",
            object?[] array => "[" + string.Join(", ", array.Select(item => Format(item, depth + 1))) + "]",
            Array array => "[" + string.Join(", ", array.Cast<object?>().Select(item => Format(item, depth + 1))) + "]",
            Dictionary<string, object?> map => "{" + string.Join(", ", map.Select(item => $"{item.Key}: {Format(item.Value, depth + 1)}")) + "}",
            Dictionary<int, object?> map => "{" + string.Join(", ", map.Select(item => $"{item.Key}: {Format(item.Value, depth + 1)}")) + "}",
            JsonElement json => FormatJsonElement(json, depth),
            _ => value.ToString() ?? ""
        };
    }

    static string FormatJsonElement(JsonElement json, int depth)
    {
        return json.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(", ", json.EnumerateObject().Select(item => $"{item.Name}: {FormatJsonElement(item.Value, depth + 1)}")) + "}",
            JsonValueKind.Array => "[" + string.Join(", ", json.EnumerateArray().Select(item => FormatJsonElement(item, depth + 1))) + "]",
            JsonValueKind.String => $"\"{json.GetString()}\"",
            JsonValueKind.Number => json.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => json.GetRawText()
        };
    }
}

