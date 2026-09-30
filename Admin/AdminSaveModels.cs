sealed record AdminSaveState(string UserId, string PublicId, string Name, string Revision,
    AdminPlayerStats Stats, AdminOwnedCard[] Cards, AdminOwnedCostume[] Costumes, AdminOwnedItem[] Items);
sealed record AdminPlayerStats(int Rank, int Exp, int RankLimit, int Stamina, int FreeJewel, int PaidJewel, int Coin);
sealed record AdminOwnedCard(string Id, long MasterId, int Level, int Awakening, bool Locked);
sealed record AdminOwnedCostume(long MasterId);
sealed record AdminOwnedItem(long MasterId, int Quantity);
sealed record AdminSaveCommand(string Revision, string Kind, long MasterId, bool Remove,
    int Quantity, AdminPlayerStats? Stats);

sealed class AdminSaveException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

static class AdminSaveErrors
{
    public const string Invalid = "ADMIN_SAVE_INVALID";
    public const string NotFound = "ADMIN_SAVE_USER_NOT_FOUND";
    public const string Conflict = "ADMIN_SAVE_CONFLICT";
    public const string InUse = "ADMIN_SAVE_IN_USE";
    public const string MasterMissing = "ADMIN_SAVE_MASTER_NOT_FOUND";
    public const string Forbidden = "ADMIN_SAVE_FORBIDDEN";
    public const string Failed = "ADMIN_SAVE_FAILED";
}
