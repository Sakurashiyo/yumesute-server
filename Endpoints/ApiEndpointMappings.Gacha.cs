static partial class ApiEndpointMappings
{
    static void MapGachaEndpoints(WebApplication app,LocalServerState state,ApiCodec codec,LocalRequestLogger logger)
    {
        app.MapGet("/localap/api/Gachas",context=>codec.WriteApiResultAsync(context,GameResults.GachaListResult()));
        app.MapGet("/localap/api/Gachas/CharacterLineup/{gachaMasterId:long}",(HttpContext context,long gachaMasterId)=>Run(context,"gacha-lineup",async ()=>await codec.WriteApiResultAsync(context,GameResults.CharacterLineupResult(gachaMasterId))));
        app.MapGet("/localap/api/Gachas/PosterLineup/{gachaMasterId:long}",(HttpContext context,long gachaMasterId)=>Run(context,"gacha-lineup",async ()=>await codec.WriteApiResultAsync(context,GameResults.PosterLineupResult(gachaMasterId))));
        app.MapMethods("/localap/api/Gachas/GetGachaHistories",["GET","POST"],context=>Run(context,"gacha-history",async ()=>await codec.WriteApiResultAsync(context,await state.UserDataService.GetGachaHistoriesAsync(context))));
        app.MapPost("/localap/api/Gachas/Roll/{gachaDetailMasterId:long}",(HttpContext context,long gachaDetailMasterId)=>Run(context,"gacha-roll",async ()=> {
            var purchase=await state.UserDataService.RollGachaAsync(context,gachaDetailMasterId);await Write(context,purchase);
        }));
        app.MapMethods("/localap/api/Shops/ExchangeShopThing/{exchangeShopThingId:long}/{quantity:int}",["GET","POST"],(HttpContext context,long exchangeShopThingId,int quantity)=>Run(context,"gacha-exchange",async ()=> {
            await Write(context,await state.UserDataService.ExchangeGachaAsync(context,[(exchangeShopThingId,quantity)]));
        }));
        app.MapPost("/localap/api/Shops/ExchangeShopThings",context=>Run(context,"gacha-exchange",async ()=> {
            var body=await codec.ReadRequestBodyAsync(context);
            if(body is not object?[] rows)throw new BadHttpRequestException(GachaErrors.Invalid,400);
            if(rows is [object?[] nested] && nested is [object?[], ..])rows=nested;
            if(rows.Length is <1 or >100)
                throw new BadHttpRequestException(GachaErrors.Invalid,400);
            var requests=rows.Select(row=> {
                if(row is not object?[] values || values.Length!=2 || !long.TryParse(Convert.ToString(values[0]),out var id) || !int.TryParse(Convert.ToString(values[1]),out var quantity))
                    throw new BadHttpRequestException(GachaErrors.Invalid,400);
                return (id,quantity);
            }).ToArray();
            await Write(context,await state.UserDataService.ExchangeGachaAsync(context,requests));
        }));
        async Task Write(HttpContext context,UserDataService.GachaPurchase purchase)=>await codec.WriteApiFramesAsync(context,purchase.Result,purchase.Present,Array.Empty<object?>(),Array.Empty<object?>(),"result-present-lz4-five-frame");
        async Task Run(HttpContext context,string operation,Func<Task> action)
        {
            try {await action();}
            catch(BadHttpRequestException error)
            {
                await logger.LogAsync($"level=WARN operation={operation} errorCode={error.Message} outcome=failed");
                context.Response.StatusCode=error.StatusCode;
                await codec.WriteApiFramesAsync(context,Array.Empty<object?>(),Array.Empty<object?>(),Array.Empty<object?>(),Array.Empty<object?>(),"five-frame");
            }
        }
    }
}
