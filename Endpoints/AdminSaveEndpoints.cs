using System.Net;
using System.Text.Json;
using Npgsql;

static class AdminSaveEndpoints
{
    public static void MapAdminSaveEndpoints(this WebApplication app, LocalServerState state)
    {
        var catalog = new AdminSaveCatalog();
        var service = new AdminSaveService(state.Database, catalog);
        var group = app.MapGroup("/admin/save-editor");
        group.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            var request = context.Request;
            var address = context.Connection.RemoteIpAddress;
            if (address is null || !IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address) ||
                request.Host.Host.ToLowerInvariant() is not ("localhost" or "127.0.0.1" or "::1" or "[::1]"))
                return Results.Json(new { code = AdminSaveErrors.Forbidden, message = "存档管理仅允许本机访问" }, statusCode: 403);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            if (HttpMethods.IsPost(request.Method) &&
                (request.Headers.Origin != $"{request.Scheme}://{request.Host}" ||
                 !request.HasJsonContentType() || request.Headers["Sec-Fetch-Site"] == "cross-site"))
                return Results.Json(new { code = AdminSaveErrors.Forbidden, message = "请从本机管理页面提交" }, statusCode: 403);
            try { return await next(invocation); }
            catch (AdminSaveException error)
            {
                app.Logger.LogWarning("存档管理拒绝操作 code={ErrorCode} path={Path}", error.Code, request.Path);
                return Results.Json(new { code = error.Code, message = error.Message }, statusCode: error.Status);
            }
            catch (JsonException) { return Results.Json(new { code = AdminSaveErrors.Invalid, message = "请求 JSON 无效" }, statusCode: 400); }
            catch (BadHttpRequestException error) { return Results.Json(new { code = AdminSaveErrors.Invalid, message = "请求格式或大小无效" }, statusCode: error.StatusCode); }
            catch (PostgresException error) when (error.SqlState == "40001")
            { return Results.Json(new { code = AdminSaveErrors.Conflict, message = "存档被并发修改，请刷新后重试" }, statusCode: 409); }
            catch (Exception error)
            {
                app.Logger.LogError(error, "存档管理失败 path={Path}", request.Path);
                return Results.Json(new { code = AdminSaveErrors.Failed, message = "操作失败，请查看服务端日志" }, statusCode: 500);
            }
        });
        group.MapGet("/", () => Asset("index.html", "text/html; charset=utf-8"));
        group.MapGet("/style.css", () => Asset("style.css", "text/css; charset=utf-8"));
        group.MapGet("/app.js", () => Asset("app.js", "text/javascript; charset=utf-8"));
        group.MapGet("/images.js", () => Asset("images.js", "text/javascript; charset=utf-8"));
        group.MapGet("/images/{kind}/{masterId:long}", (string kind, long masterId) =>
        {
            var entries = kind switch { "card" => catalog.Cards, "costume" => catalog.Costumes, "item" => catalog.Items, _ => null };
            if (entries is null || !entries.TryGetValue(masterId, out var entry)) return Results.NotFound();
            // URL 只接收主数据 ID，文件名也必须是安全的图标键，不接受任意资源路径。
            if (!System.Text.RegularExpressions.Regex.IsMatch(entry.ImageKey, "^[0-9_]+$")) throw new InvalidOperationException("图标主数据键无效");
            var file = Path.Combine(AppContext.BaseDirectory, "AdminIcons", kind, entry.ImageKey + ".png");
            return File.Exists(file) ? Results.File(file, "image/png") : Results.NotFound();
        });
        group.MapGet("/api/catalog", () => new { cards = catalog.Cards.Values, costumes = catalog.Costumes.Values, items = catalog.Items.Values });
        group.MapGet("/api/users", (string? q) => service.ListUsersAsync(q ?? ""));
        group.MapGet("/api/users/{userId:long}", (long userId) => service.ReadAsync(userId));
        group.MapPost("/api/users/{userId:long}", async (long userId, HttpContext context) =>
        {
            var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = 32768;
            var command = await context.Request.ReadFromJsonAsync<AdminSaveCommand>()
                ?? throw new AdminSaveException(AdminSaveErrors.Invalid, "缺少存档操作");
            var saved = await service.SaveAsync(userId, command);
            app.Logger.LogInformation("存档管理保存成功 userId={UserId} operation={Operation} masterId={MasterId}", userId, command.Kind, command.MasterId);
            return saved;
        });
    }

    static IResult Asset(string name, string contentType) => Results.Stream(
        typeof(AdminSaveEndpoints).Assembly.GetManifestResourceStream($"AdminWeb.{name}")
            ?? throw new InvalidOperationException($"缺少管理页面资源：{name}"), contentType);
}
