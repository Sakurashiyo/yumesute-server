using System.Text.Json;

static class AdminOperationsEndpoints
{
    public static void MapAdminOperationsEndpoints(this WebApplication app, LocalServerState state)
    {
        var group = app.MapGroup("/admin/operations");
        group.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            if (LocalAdminAccess.Check(context, GameOperationsErrors.Forbidden) is { } denied) return denied;
            try { return await next(invocation); }
            catch (GameOperationsException error)
            {
                app.Logger.LogWarning("活动配置拒绝操作 errorCode={ErrorCode} requestId={RequestId}", error.Code, context.TraceIdentifier);
                return Results.Json(new { code = error.Code, message = error.Message }, statusCode: error.Status);
            }
            catch (Exception error) when (error is JsonException or BadHttpRequestException)
            {
                app.Logger.LogWarning("活动配置格式无效 errorCode={ErrorCode} requestId={RequestId}", GameOperationsErrors.Invalid, context.TraceIdentifier);
                return Results.Json(new { code = GameOperationsErrors.Invalid, message = "配置格式无效，请刷新页面后重试" }, statusCode: 400);
            }
            catch (Exception error)
            {
                app.Logger.LogError(error, "活动配置保存失败 errorCode={ErrorCode} requestId={RequestId}", GameOperationsErrors.Failed, context.TraceIdentifier);
                return Results.Json(new { code = GameOperationsErrors.Failed, message = "操作失败，请查看服务端日志" }, statusCode: 500);
            }
        });
        group.MapGet("/", () => Asset("operations.html", "text/html; charset=utf-8"));
        group.MapGet("/operations.css", () => Asset("operations.css", "text/css; charset=utf-8"));
        group.MapGet("/operations.js", () => Asset("operations.js", "text/javascript; charset=utf-8"));
        group.MapGet("/operations-rewards.js", () => Asset("operations-rewards.js", "text/javascript; charset=utf-8"));
        group.MapOperationsNotifications(state, app.Logger);
        group.MapGet("/api/config", async () => Results.Json(await state.GameOperations.GetAsync(), GameOperationsJson.Options));
        group.MapGet("/api/catalog", () =>
        {
            var inventory = new AdminSaveCatalog();
            return new
            {
                missionPass = new { id = MissionPassRules.Id, name = "Mission Pass · 2026 年 7 月", pointItemId = MissionPassRules.PointItemId },
                gachas = GachaRules.AllPools.Select(pool => new { id = pool.Id, name = pool.Name, type = pool.Type,
                    historical = pool.Historical, available = HistoricalGachaResources.Available(pool,state.Config.ResourcesRoot), count = pool.Entries.Length }),
                loginBonuses = LoginBonusRules.Rewards.Select(reward => new { reward.Id, reward.Title, reward.ThingType, reward.MasterId, reward.Quantity, reward.Days, reward.IsLoop }),
                items = inventory.Items.Values.Select(item => new { item.Id, item.Name, item.MaxQuantity }),
                posters = GachaRules.ReadRows("Shop.PosterMaster.json").Cast<object?[]>().Select(row => new { id = GachaRules.Long(row[0]), name = Convert.ToString(row[1]) })
            };
        });
        group.MapPost("/api/config", async (HttpContext context) =>
        {
            var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = 98304;
            var request = await context.Request.ReadFromJsonAsync<GameOperationsSave>(GameOperationsJson.Options)
                ?? throw new GameOperationsException(GameOperationsErrors.Invalid, "缺少配置");
            await state.MasterDataService.ValidateOperationsAsync(request.Configuration);
            var saved = await state.GameOperations.SaveAsync(request);
            app.Logger.LogInformation(
                "活动配置保存完成 operation={Operation} revision={Revision} passEnabled={PassEnabled} spEnabled={SpEnabled} requestId={RequestId}",
                "operations-save", saved.Revision, saved.Configuration.MissionPass.Enabled, saved.Configuration.MissionPass.SpEnabled, context.TraceIdentifier);
            return Results.Json(saved, GameOperationsJson.Options);
        });
    }

    static IResult Asset(string name, string contentType) => Results.Stream(
        typeof(AdminOperationsEndpoints).Assembly.GetManifestResourceStream($"AdminWeb.{name}")
            ?? throw new InvalidOperationException($"缺少活动管理页面资源：{name}"), contentType);
}
