using System.Collections.Concurrent;

// 进程内限流与引继来源锁定。
// 单实例本地服务不需要分布式计数；重启清空计数是可接受的。
static class RequestGuard
{
    sealed class FixedWindow(int limit, TimeSpan span)
    {
        readonly ConcurrentDictionary<string, (DateTimeOffset Start, int Count)> buckets = new();

        public bool TryAcquire(string key)
        {
            var now = DateTimeOffset.UtcNow;
            while (true)
            {
                var current = buckets.GetOrAdd(key, _ => (now, 0));
                if (now - current.Start >= span)
                {
                    if (buckets.TryUpdate(key, (now, 1), current)) return true;
                    continue;
                }
                if (current.Count >= limit) return false;
                if (buckets.TryUpdate(key, (current.Start, current.Count + 1), current)) return true;
            }
        }
    }

    // 失败计数窗口：到期自动清零，不需要显式释放。
    sealed class FailureWindow(int limit, TimeSpan span)
    {
        readonly ConcurrentDictionary<string, (DateTimeOffset Start, int Count)> buckets = new();

        public bool IsLocked(string key)
        {
            if (!buckets.TryGetValue(key, out var current)) return false;
            return DateTimeOffset.UtcNow - current.Start < span && current.Count >= limit;
        }

        public void RecordFailure(string key)
        {
            var now = DateTimeOffset.UtcNow;
            buckets.AddOrUpdate(key, _ => (now, 1),
                (_, current) => now - current.Start >= span ? (now, 1) : (current.Start, current.Count + 1));
        }

        public void Reset(string key) => buckets.TryRemove(key, out _);
    }

    static readonly FixedWindow Registration = new(limit: 5, span: TimeSpan.FromMinutes(10));
    static readonly FailureWindow TakeOverFailures = new(limit: 5, span: TimeSpan.FromMinutes(5));

    // 反代后 RemoteIpAddress 全是 127.0.0.1，必须先由 UseForwardedHeaders 还原。
    static string ClientKey(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.ToString();
    }

    // 注册是匿名且每次签发长寿命令牌并写入整套默认数据，必须按来源限流。
    public static void GuardRegistration(HttpContext context)
    {
        if (!Registration.TryAcquire($"register:{ClientKey(context)}"))
            throw new BadHttpRequestException(AccountErrors.RegistrationRateLimited, StatusCodes.Status429TooManyRequests);
    }

    // 引继锁按 (引继码, 来源 IP)，避免知道引继码的人反复打错密码把号主永久锁死。
    public static string TakeOverKey(HttpContext context, string linkageCode) =>
        $"takeover:{linkageCode}:{ClientKey(context)}";

    public static void AssertTakeOverNotLocked(string key)
    {
        if (TakeOverFailures.IsLocked(key))
            throw new BadHttpRequestException(AccountErrors.RecoveryRateLimited, StatusCodes.Status429TooManyRequests);
    }

    public static void RecordTakeOverFailure(string key) => TakeOverFailures.RecordFailure(key);

    public static void ClearTakeOverFailures(string key) => TakeOverFailures.Reset(key);
}
