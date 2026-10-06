static partial class ApiEndpointMappings
{
    static void MapMusicExchangeEndpoints(WebApplication app,LocalServerState state,ApiCodec codec,LocalRequestLogger logger)
    {
        async Task Exchange(HttpContext context,long id,bool score)
        {
            try
            {
                var queryName = score?"mLiveId":"mMusicId";
                if (context.Request.Query.TryGetValue(queryName,out var query) &&
                    (!long.TryParse(query,out var requested) || requested!=id))
                    throw new BadHttpRequestException(MusicUnlockErrors.InvalidRequest);
                var present = await state.UserDataService.ExchangeMusicAsync(context,id,score);
                await codec.WriteApiFramesAsync(context,score?new object?[]{true}:GameResults.ExchangeMusicResult(id),
                    present,Array.Empty<object?>(),Array.Empty<object?>(),"result-present-lz4-five-frame");
            }
            catch (BadHttpRequestException error)
            {
                await logger.LogAsync($"level=WARN operation=music-exchange id={id} score={score} errorCode={error.Message} outcome=failed");
                context.Response.StatusCode=error.StatusCode;
                await codec.WriteApiFramesAsync(context,new object?[]{false},Array.Empty<object?>(),Array.Empty<object?>(),Array.Empty<object?>(),"five-frame");
            }
        }
        app.MapMethods("/localap/api/Shops/ExchangeMusic/{musicMasterId:long}",new[]{"GET","POST"},
            (HttpContext context,long musicMasterId)=>Exchange(context,musicMasterId,false));
        app.MapMethods("/localap/api/Shops/ExchangeMusicScore/{liveMasterId:long}",new[]{"GET","POST"},
            (HttpContext context,long liveMasterId)=>Exchange(context,liveMasterId,true));
        app.MapMethods("/localap/api/Shops/ExchangeMusicScoreAsync/{liveMasterId:long}",new[]{"GET","POST"},
            (HttpContext context,long liveMasterId)=>Exchange(context,liveMasterId,true));
    }
}