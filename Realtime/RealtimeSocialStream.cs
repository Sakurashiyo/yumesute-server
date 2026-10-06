using Grpc.Core;

sealed partial class RealtimeHubService
{
    async Task RunSocialStreamAsync(long userId, string identity, long authSessionId, string hub, IAsyncStreamReader<byte[]> requests,
        IServerStreamWriter<byte[]> responses, ServerCallContext call, ILogger logger)
    {
        var session = state.SocialRealtime.Attach(userId, identity, hub, authSessionId);
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(call.CancellationToken, session.Stop.Token);
        async Task SendAsync()
        {
            try
            {
                await foreach (var frame in session.Outgoing.Reader.ReadAllAsync(stopped.Token))
                    await responses.WriteAsync(frame, stopped.Token);
            }
            finally { session.Cancel(); }
        }
        var sender = SendAsync();
        try
        {
            while (await requests.MoveNext(stopped.Token))
            {
                await AuthenticateAsync(call);
                await state.SocialRealtime.DispatchAsync(session, requests.Current, logger, stopped.Token);
            }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            logger.LogDebug("社交连接取消 hub={Hub} userId={UserId}", hub, userId);
        }
        finally
        {
            state.SocialRealtime.Detach(session);
            stopped.Cancel();
            try { await sender; }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            session.Dispose();
            logger.LogInformation("社交连接关闭 hub={Hub} userId={UserId}", hub, userId);
        }
    }
}
