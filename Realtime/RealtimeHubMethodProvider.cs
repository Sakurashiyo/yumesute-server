using Grpc.AspNetCore.Server.Model;
using Grpc.Core;

sealed class RealtimeHubMethodProvider : IServiceMethodProvider<RealtimeHubService>
{
    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<RealtimeHubService> context)
    {
        // 客户端使用接口名作为 gRPC service，MessagePack 原始负载不经过 Protobuf。
        var marshaller = Marshallers.Create<byte[]>(value => value, value => value);
        foreach (var hub in new[] { "ICommonHub", "ICircleHub", "IMultiLiveHub" })
        {
            var method = new Method<byte[], byte[]>(MethodType.DuplexStreaming, hub, "Connect", marshaller, marshaller);
            context.AddDuplexStreamingMethod(method, Array.Empty<object>(),
                (service, request, response, call) => service.ConnectAsync(hub, request, response, call));
        }
    }
}
