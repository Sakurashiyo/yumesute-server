using System.Text.Json;

static class DefaultUserState
{
    public static string CreateJson(string publicId, string name)
    {
        var state = new Dictionary<string, object?>
        {
            ["profile"] = new Dictionary<string, object?>
            {
                ["publicId"] = publicId,
                ["name"] = name,
                ["rank"] = 1,
                ["exp"] = 0,
                ["stamina"] = 200,
                ["staminaMax"] = 50,
                ["tutorialStatus"] = 99
            },
            ["wallet"] = new Dictionary<string, object?>
            {
                ["freeJewel"] = 0,
                ["paidJewel"] = 0,
                ["coin"] = 0
            },
            ["inventory"] = new Dictionary<string, object?>
            {
                ["items"] = new Dictionary<string, int>(),
                ["characters"] = new[] { 110010, 110020, 110030, 110040, 110050 },
                ["posters"] = Array.Empty<int>(),
                ["accessories"] = Array.Empty<int>(),
                ["musics"] = Array.Empty<int>(),
                ["stamps"] = new[] { 1, 2, 3 }
            },
            ["parties"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = 1,
                    ["name"] = "Main",
                    ["leaderPosition"] = 1,
                    ["slots"] = new[] { 110010, 110020, 110030, 110040, 110050 }
                        .Select((characterId, index) => new Dictionary<string, object?>
                        {
                            ["position"] = index + 1,
                            ["characterId"] = characterId,
                            ["posterId"] = null,
                            ["accessoryIds"] = Array.Empty<int>()
                        })
                        .ToArray()
                }
            },
            ["liveRecords"] = new Dictionary<string, object?>(),
            ["inbox"] = Array.Empty<object?>(),
            ["flags"] = new Dictionary<string, object?>()
        };

        return JsonSerializer.Serialize(state);
    }
}

