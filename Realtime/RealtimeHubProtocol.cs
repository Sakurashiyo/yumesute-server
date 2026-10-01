using Grpc.Core;
using System.Text;
using System.Buffers.Binary;

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

    public static object? ReadArguments(byte[] payload)
    {
        try
        {
            ValidateValue(payload);
            var frame = (object?[])MsgPack.Decode(payload)!;
            return MagicOnionLz4.Unwrap(frame[^1], 256 * 1024, bytes =>
            {
                ValidateValue(bytes);
                return MsgPack.Decode(bytes);
            });
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or InvalidOperationException
            or IndexOutOfRangeException or EndOfStreamException or OverflowException or ArgumentException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, InvalidFrame));
        }
    }

    // 在分配和解码前验证完整长度与嵌套深度，拒绝截断、尾随数据和压缩后的深层对象。
    static void ValidateValue(byte[] bytes)
    {
        var offset = 0;
        uint Length(int size)
        {
            if (offset + size > bytes.Length) throw new InvalidDataException("长度字段不完整");
            var value = size switch
            {
                1 => bytes[offset],
                2 => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset)),
                4 => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)),
                _ => throw new InvalidOperationException()
            };
            offset += size;
            return value;
        }
        void Value(int depth)
        {
            if (depth > 32 || offset >= bytes.Length) throw new InvalidDataException("对象深度或长度非法");
            var marker = bytes[offset++];
            long skip = 0, children = 0;
            if (marker <= 0x7f || marker >= 0xe0 || marker is 0xc0 or 0xc2 or 0xc3) return;
            if (marker is >= 0x90 and <= 0x9f) children = marker & 15;
            else if (marker is >= 0x80 and <= 0x8f) children = (marker & 15) * 2;
            else if (marker is >= 0xa0 and <= 0xbf) skip = marker & 31;
            else switch (marker)
                {
                    case 0xcc: case 0xd0: skip = 1; break;
                    case 0xcd: case 0xd1: skip = 2; break;
                    case 0xce: case 0xd2: case 0xca: skip = 4; break;
                    case 0xcf: case 0xd3: case 0xcb: skip = 8; break;
                    case 0xd9: case 0xc4: skip = Length(1); break;
                    case 0xda: case 0xc5: skip = Length(2); break;
                    case 0xdb: case 0xc6: skip = Length(4); break;
                    case 0xd4: skip = 2; break;
                    case 0xd5: skip = 3; break;
                    case 0xd6: skip = 5; break;
                    case 0xd7: skip = 9; break;
                    case 0xd8: skip = 17; break;
                    case 0xc7: skip = Length(1) + 1L; break;
                    case 0xc8: skip = Length(2) + 1L; break;
                    case 0xc9: skip = Length(4) + 1L; break;
                    case 0xdc: children = Length(2); break;
                    case 0xdd: children = Length(4); break;
                    case 0xde: children = Length(2) * 2L; break;
                    case 0xdf: children = Length(4) * 2L; break;
                    default: throw new InvalidDataException("标记非法");
                }
            if (skip > bytes.Length - offset || children > bytes.Length - offset)
                throw new InvalidDataException("对象数据不完整");
            offset += (int)skip;
            for (long i = 0; i < children; i++) Value(depth + 1);
        }
        Value(0);
        if (offset != bytes.Length) throw new InvalidDataException("存在尾随数据");
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
