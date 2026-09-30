static partial class GameResults
{
    public static object?[] GeneratePhotoResult(long photoId, string fileName, string sasToken)
    {
        return new object?[] { photoId, fileName, sasToken, 5, null, null };
    }

    public static object?[] GeneratePhotoPresentData(long photoId, string fileName, string sasToken, long itemMasterId)
    {
        var now = DateTime.UtcNow;
        var thumbnailSasToken = BuildPhotoSasToken("thumbnail");

        return new object?[]
        {
            DataObject(123, new object?[]
            {
                photoId,
                fileName,
                sasToken,
                null,
                false,
                null,
                1,
                5,
                null,
                now,
                thumbnailSasToken,
                new object?[] { 404, 104 },
                new object?[] { 404, 104 },
                0
            }),
            DataObject(27, new object?[] { Random.Shared.Next(151000000, 151999999), itemMasterId, 0 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 1, null, 200300, 200301 }),
            DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 1, null, 10, 10 }),
            DataObject(48, new object?[] { 126984864L, false, false, 3, null, 12, 12 }),
            DataObject(48, new object?[] { 126984517L, false, false, 11, null, 42, 42 }),
            DataObject(96, new object?[] { Random.Shared.Next(260000000, 260999999), 404, 9, 10501, 1, 1, 0, 0 })
        };
    }

    public static object?[] GeneratePhotoNotifications()
    {
        return new object?[]
        {
            new object?[] { 0, new object?[] { 10, 10 } }
        };
    }

    public static string BuildPhotoSasToken(string purpose)
    {
        var signature = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        return $"?sv=local&se=2125-07-22T17%3A25%3A50Z&sr=b&sp=r&sig={Uri.EscapeDataString($"{purpose}:{signature}")}";
    }
}
