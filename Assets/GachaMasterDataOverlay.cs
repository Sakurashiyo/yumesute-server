using System.Security.Cryptography;
using System.Buffers.Binary;

static class GachaMasterDataOverlay
{
    // 发布标记也递增，兼容只比较发布时间的客户端缓存。
    const long PublishRevision = 2;
    static readonly object Gate = new();
    static readonly Dictionary<string,MasterDataVersionInfo> Cache = new();
    public static MasterDataVersionInfo Apply(MasterDataVersionInfo source)
    {
        var path=Path.GetFullPath(source.LocalFilePath);
        var info=new FileInfo(path);
        if(!info.Exists)throw new FileNotFoundException("统一奖池需要有效的原始 MasterMemory 文件，请检查 MASTER_DATA_FILE",path);
        var key=$"{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}|{source.Version}|{source.PublishTimestamp}";
        lock(Gate)
        {
            if(Cache.TryGetValue(key,out var result))return result;
            var bytes=Build(File.ReadAllBytes(path));
            var hash=Convert.ToHexStringLower(SHA256.HashData(bytes))[..12];
            var filename=$"mastermemory_{source.PublishTimestamp+PublishRevision}_{hash}.db";
            var folder=Path.Combine(Path.GetDirectoryName(path)!,"generated-gacha");
            Directory.CreateDirectory(folder);
            var output=Path.Combine(folder,filename);
            if(!File.Exists(output))
            {
                var temp=output+"."+Guid.NewGuid().ToString("N")+".tmp";
                File.WriteAllBytes(temp,bytes);File.Move(temp,output,true);
            }
            result=source with {Version=source.Version+"-"+hash,PublishTimestamp=source.PublishTimestamp+PublishRevision,
                RemotePath=source.RemotePath[..(source.RemotePath.LastIndexOf('/')+1)]+filename,LocalFilePath=output};
            Cache.Clear();Cache.Add(key,result);return result;
        }
    }
    internal static byte[] Build(byte[] original)
    {
        var header=MsgPack.DecodePrefix(original,out var headerLength) as Dictionary<string,object?>
            ?? throw new InvalidDataException("MasterMemory 索引不是字典");
        using var body=new MemoryStream();
        var output=new Dictionary<string,object?>();
        foreach(var pair in header)
        {
            var range=(object?[])pair.Value!;
            var offset=checked((int)GachaRules.Long(range[0]));
            var length=checked((int)GachaRules.Long(range[1]));
            if(offset<0 || length<=0 || (long)headerLength+offset+length>original.Length)
                throw new InvalidDataException("MasterMemory 表索引越界");
            var bytes=original.AsSpan(headerLength+offset,length).ToArray();
            if(pair.Key is "GachaMaster" or "ExchangeShopMaster" or "ItemMaster")
            {
                var rows=MagicOnionLz4.Unwrap(MsgPack.Decode(bytes),16*1024*1024) as object?[]
                    ?? throw new InvalidDataException("MasterMemory 表不是数组");
                rows=pair.Key switch {"GachaMaster"=>GachaRules.MasterGachas(rows),"ExchangeShopMaster"=>GachaRules.MasterShops(rows),_=>GachaRules.MasterItems(rows)};
                // MasterMemory 使用单块 LZ4 扩展 99，保持客户端现有读取约定。
                var raw=MsgPack.Encode(rows);
                var size=new byte[5];size[0]=0xd2;BinaryPrimitives.WriteInt32BigEndian(size.AsSpan(1),raw.Length);
                bytes=MsgPack.Encode(new MsgPack.Ext(99,size.Concat(MagicOnionLz4.CompressLiteralBlock(raw)).ToArray()));
            }
            output.Add(pair.Key,new object?[] {checked((int)body.Length),bytes.Length});body.Write(bytes);
        }
        if(!new[] {"GachaMaster","ExchangeShopMaster","ItemMaster"}.All(output.ContainsKey))
            throw new InvalidDataException("MasterMemory 缺少抽卡所需表");
        return MsgPack.Encode(output).Concat(body.ToArray()).ToArray();
    }
}
