using SiriusLocalServer.Realtime;

sealed partial class MultiLiveRealtimeService
{
    internal sealed record Recruitment(string HallId, int Type, int Count, string HostIdentity);
    internal Recruitment? RecruitmentFor(string identity)
    {
        lock (SyncRoot)
        {
            PruneDisconnected();
            var room = GetRoomForUser(identity);
            if (room is null || room.Status is not (MultiLiveHallStatus.Recruiting or MultiLiveHallStatus.StandbyGame) || room.Users.Count >= 4) return null;
            return new(room.HallId, room.HallType == MultiLiveHallType.TeamChallenge ? 2 : 1, room.Users.Count, room.Users[room.HostMemberId].HashUserId);
        }
    }
    internal bool IsActive(LobbyConnection session)
    {
        lock (SyncRoot) return connections.GetValueOrDefault(session.Identity) == session && !session.Stop.IsCancellationRequested;
    }
}
