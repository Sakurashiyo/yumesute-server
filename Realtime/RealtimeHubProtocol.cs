using Grpc.Core;
using System.Text;

static class RealtimeHubProtocol
{
    public const string InvalidFrame = "REALTIME_INVALID_FRAME";
    public const string NotJoined = "REALTIME_NOT_JOINED";
    public const string MethodUnimplemented = "REALTIME_METHOD_UNIMPLEMENTED";
    public const string AuthRequired = "REALTIME_AUTH_REQUIRED";
    public const string AuthInvalid = "REALTIME_AUTH_INVALID";
    public static int MethodId(string name)
    {
        var hash = 2166136261u;
        foreach (var value in Encoding.UTF8.GetBytes(name)) hash = unchecked((hash ^ value) * 16777619);
        return unchecked((int)hash);
    }

    public static (int MessageId, int MethodId) ReadRequest(byte[] payload)
    {
        try
        {
            if (payload.Length < 3 || payload[0] is not (0x92 or 0x93))
                throw new FormatException("请求帧结构不合法");
            // 仅解析协议编号；未知方法的参数不反序列化，避免深层对象或恶意长度分配。
            var offset = 1;
            var hasResponse = payload[0] == 0x93;
            var messageId = hasResponse ? ReadNumber(payload, ref offset) : -1;
            var methodId = ReadNumber(payload, ref offset);
            if (offset >= payload.Length || (hasResponse && messageId < 0))
                throw new FormatException("缺少参数或请求编号不合法");
            var parameterless = methodId == MethodId("JoinAsync") || methodId == MethodId("GetMultiLiveInvitationsFromFriendAsync") || methodId == MethodId("GetActivityLogsAsync");
            if (parameterless && (offset != payload.Length - 1 || payload[offset] != 0xc0))
                throw new FormatException("无参方法必须使用 nil 参数");
            return (messageId, methodId);
        }
        catch (Exception error) when (error is FormatException or InvalidOperationException or IndexOutOfRangeException or EndOfStreamException or OverflowException or ArgumentException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, InvalidFrame));
        }
    }

    static int ReadNumber(byte[] payload, ref int offset)
    {
        var marker = payload[offset];
        var size = marker switch
        {
            <= 0x7f or >= 0xe0 => 1,
            0xcc or 0xd0 => 2,
            0xcd or 0xd1 => 3,
            0xce or 0xd2 => 5,
            0xd3 => 9,
            _ => throw new FormatException("编号必须为整数")
        };
        var value = MsgPack.Decode(payload.AsSpan(offset, size).ToArray());
        offset += size;
        return ReadInt(value);
    }

    static int ReadInt(object? value) => value switch
    {
        byte number => number,
        sbyte number => number,
        short number => number,
        ushort number => number,
        uint number when number <= int.MaxValue => (int)number,
        int number => number,
        long number when number >= int.MinValue && number <= int.MaxValue => (int)number,
        _ => throw new FormatException("方法和请求编号必须为整数")
    };

    public static byte[] Response(int messageId, int methodId, object? value) => MsgPack.Encode(new object?[] { messageId, methodId, value });
    public static byte[] Broadcast(string name, object? value) => MsgPack.Encode(new object?[] { MethodId(name), value });
    public static byte[] Error(int messageId, StatusCode status, string code) => MsgPack.Encode(new object?[] { messageId, (int)status, code, null });
}
