static partial class ApiEndpointMappings
{
    static void MapPermanentMarketEndpoint(WebApplication app,LocalServerState state,ApiCodec codec,LocalRequestLogger logger)
    {
        app.MapPost("/localap/api/Shops/ExchangePermanentMarketThing/{permanentMarketThingMasterId:long}",async (HttpContext context,long permanentMarketThingMasterId) =>
        {
            try
            {
                if(!int.TryParse(context.Request.Query["quantity"],out var quantity)
                    || context.Request.Query.TryGetValue("permanentMarketThingMasterId",out var requested)
                    && (!long.TryParse(requested,out var requestedId) || requestedId!=permanentMarketThingMasterId))
                    throw new BadHttpRequestException(PermanentMarketErrors.InvalidRequest,400);
                var exchange = await state.UserDataService.ExchangePermanentMarketThingAsync(context,permanentMarketThingMasterId,quantity);
                await codec.WriteApiFramesAsync(context,exchange.Rewards,exchange.Present,Array.Empty<object?>(),Array.Empty<object?>(),"result-present-lz4-five-frame");
            }
            catch(BadHttpRequestException error)
            {
                await logger.LogAsync($"level=WARN operation=permanent-market-exchange productId={permanentMarketThingMasterId} errorCode={error.Message} outcome=failed");
                context.Response.StatusCode=error.StatusCode;
                await codec.WriteApiFramesAsync(context,Array.Empty<object?>(),Array.Empty<object?>(),Array.Empty<object?>(),Array.Empty<object?>(),"five-frame");
            }
        });
    }
}
