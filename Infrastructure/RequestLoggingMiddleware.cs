static class RequestLoggingMiddleware
{
    public static IApplicationBuilder UseRealtimeRequestLogging(this IApplicationBuilder app, ILogger logger, int port)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Connection.LocalPort != port)
            {
                await next();
                return;
            }

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            logger.LogInformation(
                "实时请求开始 method={Method} path={Path} protocol={Protocol} connectionId={ConnectionId} requestId={RequestId}",
                context.Request.Method, context.Request.Path, context.Request.Protocol,
                context.Connection.Id, context.TraceIdentifier);
            try
            {
                await next();
            }
            finally
            {
                logger.LogInformation(
                    "实时请求结束 path={Path} statusCode={StatusCode} durationMs={DurationMs} connectionId={ConnectionId} requestId={RequestId}",
                    context.Request.Path, context.Response.StatusCode,
                    System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    context.Connection.Id, context.TraceIdentifier);
            }
        });
    }

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

