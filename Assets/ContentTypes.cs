static class ContentTypes
{
    public static string Guess(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".hash" => "text/plain",
            ".bundle" => "application/octet-stream",
            ".db" => "application/octet-stream",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}

