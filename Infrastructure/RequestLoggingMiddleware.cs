static class RequestLoggingMiddleware
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app, LocalRequestLogger logger)
    {
        return app.Use(async (context, next) =>
        {
            await logger.LogAsync($"{context.Request.Method} {context.Request.Path}{context.Request.QueryString} content-type={context.Request.ContentType ?? ""} accept={context.Request.Headers.Accept}");
            try
            {
                await next();
                await logger.LogAsync($"-> {context.Response.StatusCode} {context.Request.Method} {context.Request.Path}");
            }
            catch (Exception ex)
            {
                await logger.LogAsync($"!! {context.Request.Method} {context.Request.Path} {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        });
    }
}

