static class PhotoMediaEndpoints
{
    public static void MapPhotoMediaEndpoints(this WebApplication app, LocalServerState state)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api/Photos") && !context.Request.Path.StartsWithSegments("/api/Photo"))
            {
                await next();
                return;
            }
            var size = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (size is { IsReadOnly: false }) size.MaxRequestBodySize = 12 * 1024 * 1024;
            try { await next(); }
            catch (BadHttpRequestException error) when (!context.Response.HasStarted)
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<UserDataService>>();
                logger.LogWarning("照片请求拒绝 operation=photo-request path={Path} errorCode={ErrorCode} statusCode={StatusCode}",
                    context.Request.Path, error.Message, error.StatusCode);
                await state.Codec.WriteApiErrorAsync(context, error.Message, error.StatusCode);
            }
        });
        app.MapMethods("/photo-content/photo/{variant}/{fileName}", new[] { "GET", "HEAD" },
            async (HttpContext context, string variant, string fileName) =>
            {
                if (variant is not ("original" or "thumbnail")) return Results.NotFound();
                var image = await state.UserDataService.ReadPhotoImageAsync(fileName, variant == "thumbnail", context.Request.Query["sig"].ToString());
                if (image is null) return Results.NotFound();
                context.Response.Headers.CacheControl = "private, max-age=3600";
                return Results.File(image, "image/jpeg");
            });
    }
}
