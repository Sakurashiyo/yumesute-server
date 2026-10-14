static partial class ApiEndpointMappings
{
    static void MapCircleOperationEndpoints(WebApplication app,LocalServerState state,ApiCodec codec)
    {
        app.MapPost("/localap/api/Circles/Edit",async context=>await codec.WriteApiResultAsync(context,
            await state.UserDataService.EditCircleAsync(context,await codec.ReadRequestBodyAsync(context)),"five-frame"));
        app.MapPost("/localap/api/Circles/AuthorityChange",async context=>
        {
            var result=await state.UserDataService.ChangeCircleAuthorityAsync(context,await codec.ReadRequestBodyAsync(context));
            await state.SocialRealtime.PublishActivitiesAsync();
            await codec.WriteApiResultAsync(context,result,"five-frame");
        });
        app.MapMethods("/localap/api/Circles/Search",new[] {"GET","POST"},async context=>await codec.WriteApiResultAsync(context,
            await state.UserDataService.SearchCirclesAsync(context,context.Request.Query["circleName"].ToString()),"five-frame"));
        app.MapPost("/localap/api/Circles/Condition",async context=>await codec.WriteApiResultAsync(context,
            await state.UserDataService.SearchCirclesAsync(context,null,await codec.ReadRequestBodyAsync(context)),"five-frame"));
        app.MapPost("/localap/api/Circles/JoinRequest",async context=>await codec.WriteApiResultAsync(context,
            await state.UserDataService.SendCircleEntryAsync(context,context.Request.Query["circleId"].ToString(),false),"five-frame"));
        app.MapPost("/localap/api/Circles/SendInvite",async context=>await codec.WriteApiResultAsync(context,
            await state.UserDataService.SendCircleEntryAsync(context,context.Request.Query["targetUserId"].ToString(),true),"five-frame"));
        foreach(var (route,invitation,action,parameter) in new[] {
            ("ApproveInvite",true,"approve","inviteId"),("ApproveRequest",false,"approve","requestId"),
            ("CancelInvite",true,"cancel","inviteId"),("RejectInvite",true,"reject","inviteId"),("RejectRequest",false,"reject","requstId")})
        {
            app.MapPost("/localap/api/Circles/"+route+"/{entryId:long}",async context=>
            {
                var id=Convert.ToInt64(context.Request.RouteValues["entryId"]);
                var processed=await state.UserDataService.ProcessCircleEntryAsync(context,id,invitation,action,
                    context.Request.Query[parameter].FirstOrDefault());
                await state.SocialRealtime.PublishActivitiesAsync();
                await codec.WriteApiFramesAsync(context,processed.Result,processed.Present,Array.Empty<object?>(),Array.Empty<object?>(),"five-frame");
            });
        }
        foreach(var route in new[] {"Expulsion","Resignation","Release"})
        {
            app.MapPost("/localap/api/Circles/"+route,async context=>
            {
                var result=await state.UserDataService.RemoveCircleMemberAsync(context,context.Request.Query["circleId"].ToString(),
                    route=="Expulsion" ? context.Request.Query["expulsionUserId"].ToString() : null,route=="Release");
                await state.SocialRealtime.PublishActivitiesAsync();
                await codec.WriteApiFramesAsync(context,result.Result,result.Present,Array.Empty<object?>(),Array.Empty<object?>(),"five-frame");
            });
        }
        app.MapPost("/localap/api/Circles/SetSupportCompany/{company}",async context=>await codec.WriteApiResultAsync(context,
            await state.UserDataService.SetCircleSupportCompanyAsync(context,context.Request.RouteValues["company"]?.ToString()),"five-frame"));
        app.MapPost("/localap/api/Circles/SetVisibleActivityLog",async context=>
        {
            var changed=await state.UserDataService.SetCircleActivityVisibilityAsync(context,context.Request.Query["isPublicActivityLog"].ToString());
            await codec.WriteApiFramesAsync(context,changed.Result,changed.Present,Array.Empty<object?>(),Array.Empty<object?>(),"five-frame");
        });
        app.MapPost("/localap/api/Circles/DonateSupportCompany",async context=>
        {
            var donation=await state.UserDataService.DonateCircleSupportAsync(context,await codec.ReadRequestBodyAsync(context));
            await codec.WriteApiFramesAsync(context,donation.Result,donation.Present,Array.Empty<object?>(),Array.Empty<object?>(),"lz4-five-frame");
        });
    }
}
