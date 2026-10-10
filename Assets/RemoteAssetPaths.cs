static class RemoteAssetPaths
{
    public static string? Resolve(LocalConfig config, string requestPath, bool addressablesOnly = false)
    {
        var parts = Uri.UnescapeDataString(requestPath).Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".." || part.Contains(':')))
            return null;

        var groupIndex = Array.FindIndex(parts, part =>
            part is "2d-assets" or "3d-assets" or "cri-assets");
        var resourceIndex = Array.FindIndex(parts, part => part == "Resources");
        var isAddressables = addressablesOnly || groupIndex >= 0 ||
            parts.Contains("com.unity.addressables") ||
            parts[^1].EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) ||
            parts[^1].EndsWith("Hashes", StringComparison.Ordinal);
        var root = isAddressables ? config.AddressablesBaseUrl : config.ResourcesBaseUrl;
        if (root is null) return null;

        string[] relative;
        if (isAddressables)
        {
            // 三类 catalog 可以同名；沿用本地目录结构，不能一起扁平化。
            if (groupIndex >= 0 && parts[^1].StartsWith("catalog_", StringComparison.Ordinal))
            {
                var extension = parts[^1].EndsWith(".hash", StringComparison.Ordinal) ? ".hash" : ".json";
                relative = [parts[groupIndex], $"catalog_{parts[groupIndex]}{extension}"];
            }
            else
            {
                var start = groupIndex >= 0 ? groupIndex + 1 : 0;
                if (start < parts.Length && parts[start] == "production") start++;
                if (start < parts.Length && parts[start] == "com.unity.addressables") start++;
                if (start < parts.Length && parts[start] == "Android") start++;
                if (start < parts.Length && IsVersion(parts[start])) start++;
                relative = parts[start..];
            }
        }
        else
        {
            relative = parts[(resourceIndex >= 0 ? resourceIndex + 1 : 0)..];
            if (relative.Length > 1 && relative[0] == "comic" && !Path.HasExtension(relative[^1]))
                relative[^1] += ".png";
        }

        if (relative.Length == 0) return null;
        return root + "/" + string.Join('/', relative.Select(Uri.EscapeDataString));
    }

    static bool IsVersion(string value) =>
        value.Contains('.') && value.All(character => char.IsDigit(character) || character == '.');
}
