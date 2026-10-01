using Grpc.Core;
using SiriusLocalServer.Realtime;

static class MultiLiveWire
{
    static readonly Lazy<AdminSaveCatalog> Catalog = new(() => new());
    public static object?[] Character(MultiLiveCharacter c) => new object?[] { c.MCharacterId, c.TalentStage,
        c.MNameColorId, c.MNamePlateId, c.MTrophyId1, c.MTrophyId2, c.MTrophyId3,
        c.DisplayAwakeningStatus, c.MNameplateDetailId, c.TotalStatus, c.MNameBaseColorid };
    public static object?[] User(MultiLiveUser u) => new object?[] { u.MemberId, u.UserName, u.IsRandomMusic,
        u.SelectedMusicId, (int)u.Status, Character(u.LeaderCharacter), u.UserNamePlateColorId,
        (int)u.Difficulty, (int)u.ClearLamp, u.Score, u.HashUserId, u.IsExitGame, u.IsAFK, u.IsReadyDecideMember };
    public static object?[] Join(MultiLiveJoinResult r, bool isPublic = false) => new object?[] {
        (int)r.ErrorCode, r.ErrorCode != MultiLiveJoinErrorCodes.None || isPublic ? null : r.HallId,
        r.MemberId, isPublic ? 0 : r.KeyCode, r.LiveSettingMasterId, r.RestrictionFinishedAt };
    public static object?[] Created(MultiLiveCreatePrivateHallResult r) => new object?[] {
        r.IsSucceeded, r.HallId, r.MemberId, r.PrivateHallKeyCode, r.RestrictionFinishedAt };
    public static object?[] Users(MultiLiveFetchUsersResult r) => new object?[] {
        r.HostMemberId, r.Users.Select(u => (object?)User(u)).ToArray(), r.CanOpenHall };

    public static RpcException Invalid() => new(new Status(StatusCode.InvalidArgument, RealtimeHubProtocol.InvalidFrame));
    public static object?[] Args(object? value, int count) => value is object?[] a && a.Length == count ? a : throw Invalid();
    public static long Number(object? value, long min = 0, long max = long.MaxValue)
    {
        var number = value switch
        {
            byte b => b,
            sbyte b => b,
            short n => n,
            ushort n => n,
            int n => n,
            uint n => n,
            long n => n,
            _ => throw Invalid()
        };
        return number >= min && number <= max ? number : throw Invalid();
    }
    public static bool Boolean(object? value) => value is bool b ? b : throw Invalid();
    public static string Text(object? value, int limit = 64) => value is string s && s.Length is > 0 && s.Length <= limit ? s : throw Invalid();
    public static MultiLiveCharacter ReadCharacter(object? value)
    {
        var a = Args(value, 11);
        long? Optional(int index) => a[index] is null ? null : Number(a[index], 1);
        var id = Number(a[0], 1);
        if (!Catalog.Value.Cards.ContainsKey(id)) throw Invalid();
        return new()
        {
            MCharacterId = id,
            TalentStage = (int)Number(a[1], 0, 6),
            MNameColorId = Optional(2),
            MNamePlateId = Optional(3),
            MTrophyId1 = Optional(4),
            MTrophyId2 = Optional(5),
            MTrophyId3 = Optional(6),
            DisplayAwakeningStatus = Boolean(a[7]),
            MNameplateDetailId = Optional(8),
            TotalStatus = (int)Number(a[9], 0, int.MaxValue),
            MNameBaseColorid = Number(a[10])
        };
    }
    // LiveSettingMaster 解包中普通协力大厅的 ID 与 HallType 相同；团队挑战暂未接入。
    public static MultiLiveHallType Hall(object? value)
    {
        var id = Number(value);
        if (id == 21) throw new RpcException(new Status(StatusCode.Unimplemented, RealtimeHubProtocol.MethodUnimplemented));
        return id is >= 11 and <= 15 ? (MultiLiveHallType)id : throw Invalid();
    }
}
