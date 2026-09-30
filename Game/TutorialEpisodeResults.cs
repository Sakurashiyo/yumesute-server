static class TutorialEpisodeResults
{
    public static object?[] Summary(long episodeMasterId)
    {
        return new object?[]
        {
            "",
            1,
            1,
            SceneAssetPath(episodeMasterId)
        };
    }

    public static object?[] Details(long episodeMasterId)
    {
        return new object?[]
        {
            Detail(episodeMasterId, 1, "Tutorial"),
            Detail(episodeMasterId, 2, "")
        };
    }

    static object?[] Detail(long episodeMasterId, int order, string title)
    {
        return new object?[]
        {
            episodeMasterId * 100 + order,
            episodeMasterId,
            order,
            order,
            "",
            "",
            2,
            "",
            title,
            "",
            "",
            null,
            "",
            "",
            "",
            "",
            null,
            null,
            "",
            Array.Empty<object?>(),
            "",
            null,
            null,
            null
        };
    }

    public static string SceneAssetPath(long episodeMasterId)
    {
        return $"scenes/{episodeMasterId}.bin";
    }
}