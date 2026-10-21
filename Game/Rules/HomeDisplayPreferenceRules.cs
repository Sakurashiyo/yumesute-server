static class HomeDisplayPreferenceRules
{
    public const string Invalid = "HOME_DISPLAY_PREFERENCE_INVALID";

    public static long Costume(GameInventoryCatalog catalog, long characterBaseId, long? costumeId)
    {
        if (!catalog.Bases.TryGetValue(characterBaseId, out var character))
            throw new BadHttpRequestException(Invalid, StatusCodes.Status400BadRequest);
        // 客户端允许省略衣装，必须取该角色的默认衣装，不能统一补成心爱的 11。
        var selected = costumeId ?? character.DefaultCostume;
        if (!catalog.CanWearCostume(selected, characterBaseId))
            throw new BadHttpRequestException(Invalid, StatusCodes.Status400BadRequest);
        return selected;
    }
}
