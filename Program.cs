DotEnv.Load(Path.Combine(AppContext.BaseDirectory, ".env"));
DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), "csharp-server", ".env"));
DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var config = LocalConfig.FromEnvironment();
var state = new LocalServerState(config);
await state.Logger.LogAsync($"config publicBaseUrl={config.PublicBaseUrl} msgpackMode={config.MsgPackResponseMode} registerMode={config.RegisterResponseMode}");

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.ConfigureKestrel(options =>
{
    var address = config.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        ? System.Net.IPAddress.Loopback : System.Net.IPAddress.Parse(config.Host);
    options.Listen(address, config.Port, listener => listener.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
    options.Listen(address, config.RealtimePort, listener =>
        listener.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});
builder.Services.AddSingleton(state);
builder.Services.AddGrpc(options => options.MaxReceiveMessageSize = 64 * 1024);
builder.Services.AddSingleton<Grpc.AspNetCore.Server.Model.IServiceMethodProvider<RealtimeHubService>, RealtimeHubMethodProvider>();

var app = builder.Build();

await DatabaseMigrator.MigrateAsync(state.Database, config);

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api", out var remaining))
    {
        context.Request.Path = "/localap/api" + remaining;
    }

    await next();
});

app.UseRouting();
app.UseRequestLogging(state.Logger);
app.MapApiEndpoints(state);
app.MapAssetEndpoints(state);
app.MapAdminNotificationEndpoints(state);
app.MapAdminSaveEndpoints(state);
app.MapRealtimeDebugEndpoints(state);
app.MapGrpcService<RealtimeHubService>();

app.MapFallback(async context =>
{
    await state.Logger.LogAsync($"?? unmatched {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    await state.Codec.WriteApiResultAsync(context, Array.Empty<object>());
});

app.Run();

