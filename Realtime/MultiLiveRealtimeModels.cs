namespace SiriusLocalServer.Realtime;

enum MultiLiveHallType
{
    None = 0,
    Sirius = 11,
    Eden = 12,
    Gingaza = 13,
    Denki = 14,
    DenkiReprint = 15,
    TeamChallenge = 21
}

enum MultiLiveTypes
{
    Default = 1,
    TeamChallenge = 2
}

enum MultiLiveUserStatus
{
    Joined = 0,
    SelectedMusic = 1,
    SelectedDifficulty = 2,
    ReadyGame = 3,
    BeforeGameCalculate = 4,
    PlayingGame = 5,
    ExitGame = 6,
    Disconnected = 7
}

enum MultiLiveHallStatus
{
    None = 0,
    Recruiting = 1,
    StandbyGame = 2,
    ReadyGame = 3,
    BeforeGameCalculate = 4,
    PlayingGame = 5
}

enum MultiLiveJoinErrorCodes
{
    None = 0,
    ReachedMaxMember = 1,
    RoomClosed = 2,
    NotFoundHall = 3,
    NotSubscribeMatchMakingServer = 4,
    UserCanceled = 5,
    Restricting = 6
}

enum MusicDifficulties
{
    None = 0,
    Normal = 1,
    Hard = 2,
    Extra = 3,
    Stella = 4,
    Olivier = 5
}

enum ClearLamps
{
    None = 0,
    Clear = 1,
    FullCombo = 2,
    FullComboMulti1 = 3,
    FullComboMulti2 = 4,
    FullComboMulti3 = 5,
    AllPerfect = 6,
    AllPerfectMulti1 = 7,
    AllPerfectMulti2 = 8,
    AllPerfectMulti3 = 9
}

enum ComboTypes
{
    None = 0,
    FullCombo = 1,
    AllPerfect = 2
}

sealed class MultiLiveCharacter
{
    public long MCharacterId { get; set; }
    public int TalentStage { get; set; }
    public long? MNameColorId { get; set; }
    public long? MNamePlateId { get; set; }
    public long? MTrophyId1 { get; set; }
    public long? MTrophyId2 { get; set; }
    public long? MTrophyId3 { get; set; }
    public bool DisplayAwakeningStatus { get; set; }
    public long? MNameplateDetailId { get; set; }
    public int TotalStatus { get; set; }
    public long MNameBaseColorid { get; set; }

    public static MultiLiveCharacter Default(long characterMasterId = 110010)
    {
        return new MultiLiveCharacter
        {
            MCharacterId = characterMasterId,
            TalentStage = 0,
            DisplayAwakeningStatus = false,
            TotalStatus = 16414,
            MNameBaseColorid = 180001
        };
    }
}

sealed class MultiLiveUser
{
    public int MemberId { get; set; }
    public string UserName { get; set; } = "";
    public bool IsRandomMusic { get; set; }
    public long SelectedMusicId { get; set; }
    public MultiLiveUserStatus Status { get; set; }
    public MultiLiveCharacter LeaderCharacter { get; set; } = MultiLiveCharacter.Default();
    public long UserNamePlateColorId { get; set; }
    public MusicDifficulties Difficulty { get; set; }
    public ClearLamps ClearLamp { get; set; }
    public long Score { get; set; }
    public string HashUserId { get; set; } = "";
    public bool IsExitGame { get; set; }
    public bool IsSelectedMusic { get; set; }
    public bool IsAFK { get; set; }
    public bool IsReadyDecideMember { get; set; }
    public Dictionary<int, int> TimingCounts { get; } = new();
    public int MaxCombo { get; set; }
    public int Life { get; set; } = 1000;
    public ComboTypes ComboType { get; set; }
    public int ComboCount { get; set; }
}

sealed class MultiLiveJoinResult
{
    public MultiLiveJoinErrorCodes ErrorCode { get; set; }
    public string HallId { get; set; } = "";
    public int MemberId { get; set; }
    public int KeyCode { get; set; }
    public long LiveSettingMasterId { get; set; }
    public DateTime? RestrictionFinishedAt { get; set; }

    public static MultiLiveJoinResult Success(string hallId, int memberId, int keyCode, long liveSettingMasterId)
    {
        return new MultiLiveJoinResult
        {
            ErrorCode = MultiLiveJoinErrorCodes.None,
            HallId = hallId,
            MemberId = memberId,
            KeyCode = keyCode,
            LiveSettingMasterId = liveSettingMasterId
        };
    }

    public static MultiLiveJoinResult Error(MultiLiveJoinErrorCodes errorCode)
    {
        return new MultiLiveJoinResult { ErrorCode = errorCode };
    }
}

sealed class MultiLiveCreatePrivateHallResult
{
    public bool IsSucceeded { get; set; }
    public string HallId { get; set; } = "";
    public int MemberId { get; set; }
    public int PrivateHallKeyCode { get; set; }
    public DateTime? RestrictionFinishedAt { get; set; }
}

sealed class MultiLiveFetchUsersResult
{
    public int HostMemberId { get; set; }
    public MultiLiveUser[] Users { get; set; } = Array.Empty<MultiLiveUser>();
    public bool CanOpenHall { get; set; }
}

sealed class MultiLiveMatchingAssignment
{
    public long LiveSettingMasterId { get; set; }
    public string GroupName { get; set; } = "";
    public string UserId { get; set; } = "";
}

sealed class MultiLiveMatchingAssignmentResult
{
    public MultiLiveMatchingAssignment[] MatchingAssignments { get; set; } = Array.Empty<MultiLiveMatchingAssignment>();
}

sealed class MultiLiveRoomState
{
    public string HallId { get; set; } = "";
    public int KeyCode { get; set; }
    public long MultiLiveId { get; set; }
    public long LiveSettingMasterId { get; set; }
    public MultiLiveHallType HallType { get; set; }
    public MultiLiveTypes MultiLiveType { get; set; } = MultiLiveTypes.Default;
    public MultiLiveHallStatus Status { get; set; } = MultiLiveHallStatus.Recruiting;
    public int HostMemberId { get; set; } = 1;
    public bool IsPrivate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<int, MultiLiveUser> Users { get; } = new();

    public MultiLiveFetchUsersResult ToFetchUsersResult()
    {
        return new MultiLiveFetchUsersResult
        {
            HostMemberId = HostMemberId,
            Users = Users.Values.OrderBy(x => x.MemberId).ToArray(),
            CanOpenHall = Users.Count >= 1
        };
    }
}
