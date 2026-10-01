using Grpc.Core;

sealed partial class RealtimeHubService
{
    async Task RunMultiLiveStreamAsync(string identity, IAsyncStreamReader<byte[]> requests,
        IServerStreamWriter<byte[]> responses, ServerCallContext call, ILogger logger)
    {
        var session = state.MultiLiveRealtime.Attach(identity);
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(call.CancellationToken, session.Stop.Token);
        // 回复和跨连接广播共用单一写入循环；gRPC 不允许并发 WriteAsync。
        async Task SendAsync()
        {
            try
            {
                await foreach (var frame in session.Outgoing.Reader.ReadAllAsync(stopped.Token))
                    await responses.WriteAsync(frame, stopped.Token);
            }
            finally { session.Stop.Cancel(); }
        }
        var sender = SendAsync();
        try
        {
            while (await requests.MoveNext(stopped.Token))
                state.MultiLiveRealtime.DispatchLobby(session, requests.Current, logger);
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            logger.LogDebug("协力连接取消 identity={Identity}", identity);
        }
        finally
        {
            state.MultiLiveRealtime.Disconnect(session);
            stopped.Cancel();
            try { await sender; }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            session.Stop.Dispose();
            logger.LogInformation("协力连接关闭 identity={Identity}", identity);
        }
    }
}
