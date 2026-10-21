using Npgsql;

sealed partial class UserDataService
{
    public async Task<string> GetLiveIdentityAsync(HttpContext context)
    {
        var userId = await GetCurrentUserIdAsync(context) ?? throw new BadHttpRequestException(AccountErrors.Unauthorized, 401);
        return await ReadPublicIdAsync(userId);
    }

    public async Task<object?[]> StartMultiLiveAsync(HttpContext context, object?[] payload, MultiLiveRealtimeService realtime)
    {
        if (payload.Length != 5 || GetLong(payload, 0) is not long chartId
            || GetLong(payload, 1) is not long liveId || payload[2] is not bool consume
            || GetLong(payload, 3) is not long ratio || ratio is < 0 or > 10
            || payload[4] is not null && (GetLong(payload, 4) is not > 0))
            throw new BadHttpRequestException(LiveProgressionErrors.InvalidStart);
        var identity = await GetLiveIdentityAsync(context);
        var setting = realtime.ValidateLiveStart(identity, liveId, chartId);
        var partyId = GetLong(payload, 4);
        if (partyId is null)
        {
            var userId = (await GetCurrentUserIdAsync(context))!.Value;
            await EnsureDefaultUserDataAsync(userId);
            await using var connection = await database.OpenConnectionAsync();
            await using var selected = new NpgsqlCommand("select selected_party_id from user_player_preferences where \"userId\"=$1", connection);
            selected.Parameters.AddWithValue(userId);
            partyId = await selected.ExecuteScalarAsync() is long current ? current
                : throw new BadHttpRequestException(PartyNotFound, 404);
        }
        // 房间 ID 加入请求摘要，相同歌曲的下一局不能重放上一局未结束的开演快照。
        var start = new object?[] { partyId, chartId, null, null, consume, ratio, setting, false };
        return (await RegisterLiveStartAsync(context, start, buildUnit: true, mode: LiveRewardRules.Mode.Multi,
            modeContext: [liveId]))!;
    }
}
