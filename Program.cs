using Microsoft.AspNetCore.HttpOverrides;

DotEnv.Load(Path.Combine(AppContext.BaseDirectory, ".env"));
DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), "csharp-server", ".env"));
DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var config = LocalConfig.FromEnvironment();
var state = new LocalServerState(config);
await state.Logger.LogAsync($"config publicBaseUrl={config.PublicBaseUrl} msgpackMode={config.MsgPackResponseMode} registerMode={config.RegisterResponseMode}");

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new RealtimeFileLoggerProvider(state.RealtimeLogger));
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

var realtimeLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Realtime");
realtimeLogger.LogInformation("实时服务初始化 host={Host} port={Port} logPath={LogPath}",
    config.Host, config.RealtimePort, Path.Combine(AppContext.BaseDirectory, "realtime.log"));
app.Lifetime.ApplicationStarted.Register(() =>
    realtimeLogger.LogInformation("实时服务已启动 host={Host} port={Port} publicUrl={PublicUrl}",
        config.Host, config.RealtimePort, config.RealtimeBaseUrl));
app.Lifetime.ApplicationStopping.Register(() => realtimeLogger.LogInformation("实时服务正在停止"));

await DatabaseMigrator.MigrateAsync(state.Database, config);

app.UseRealtimeRequestLogging(realtimeLogger, config.RealtimePort);
// 反代后 RemoteIpAddress 全是回环地址，限流与引继锁定必须依赖转发头还原真实来源。
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
    KnownIPNetworks = { },
    KnownProxies = { }
});
app.UseRouting();
app.UseRequestLogging(state.Logger);
app.MapApiEndpoints(state);
app.MapAssetEndpoints(state);
app.MapRealtimeDebugEndpoints(state);
app.MapGrpcService<RealtimeHubService>();

app.MapFallback(async context =>
{
    await state.Logger.LogAsync($"?? unmatched {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    await state.Codec.WriteApiResultAsync(context, Array.Empty<object>());
});

app.Run();

