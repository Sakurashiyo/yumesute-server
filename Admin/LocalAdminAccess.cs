using System.Net;

static class LocalAdminAccess
{
    public static IResult? Check(HttpContext context, string errorCode)
    {
        var request = context.Request;
        var address = context.Connection.RemoteIpAddress;
        if (address is null || !IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)
            || request.Host.Host.ToLowerInvariant() is not ("localhost" or "127.0.0.1" or "::1" or "[::1]"))
            return Results.Json(new { code = errorCode, message = "管理页面仅允许本机访问" }, statusCode: 403);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        if (HttpMethods.IsPost(request.Method)
            && (request.Headers.Origin != $"{request.Scheme}://{request.Host}" || !request.HasJsonContentType()
                || request.Headers["Sec-Fetch-Site"] == "cross-site"))
            return Results.Json(new { code = errorCode, message = "请从本机管理页面提交" }, statusCode: 403);
        return null;
    }
}
