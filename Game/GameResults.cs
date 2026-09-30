using System.Text.Json;

static partial class GameResults
{
    const long DefaultHomeSkinMasterId = 200001L;
    const long DefaultNameBaseColorMasterId = 180001L;
    const long DefaultIconFrameMasterId = 190001L;
    const long DefaultCharacterBaseMasterId = 101L;
    const long DefaultCharacterMasterId = 110010L;
    const long DefaultCharacterBaseId = 1L;
    const long DefaultCharacterId = 1L;
    const long DefaultCostumeId = 1L;
    const long DefaultCostumeMasterId = 11L;
    const long DefaultHomeBgmMasterId = 1L;
    const long DefaultHomeBgmDetailMasterId = 1001L;
    static long LastLiveMasterId;

    public static object?[] Environment(LocalConfig config)
    {
        return new object?[]
        {
            config.ApplicationVersion,
            config.AssetVersion,
            config.PublicBaseUrl,
            null,
            null,
            false,
            $"{config.PublicBaseUrl}/master",
            config.StaticContentUrl,
            $"{config.PublicBaseUrl}/localassets",
            false,
            $"{config.PublicBaseUrl}/photo-content",
            config.RealtimeBaseUrl,
            $"{config.PublicBaseUrl}/payment-disabled"
        };
    }

    public static object?[] EmptyLogin()
    {
        return new object?[]
        {
            Array.Empty<object?>(),
            0,
            false,
            Array.Empty<object?>(),
            Array.Empty<object?>(),
            Array.Empty<object?>(),
            Array.Empty<object?>()
        };
    }

    public static object?[] EmptyLoginBonusResults()
    {
        return Array.Empty<object?>();
    }

    public static object?[] MasterDataManifest(LocalConfig config)
    {
        return new object?[]
        {
            config.MasterDataRemotePath,
            "",
            config.MasterDataVersion,
            config.MasterDataPublishTimestamp
        };
    }

    public static object?[] MasterDataManifest(MasterDataVersionInfo masterData)
    {
        return new object?[]
        {
            masterData.RemotePath,
            masterData.SasToken,
            masterData.Version,
            masterData.PublishTimestamp
        };
    }

    public static object?[] EmptyPagedList()
    {
        return new object?[] { Array.Empty<object?>(), 0 };
    }

    public static object?[] UserData()
    {
        var now = DateTime.UtcNow;
        var user = new object?[]
        {
            1L,
            1,
            0,
            100,
            now.AddHours(1),
            0,
            0,
            10000,
            999,
            0,
            DateTime.UnixEpoch,
            "",
            now,
            "local-user",
            0,
            99,
            0,
            DateTime.UnixEpoch,
            false,
            false
        };

        var userProfile = new object?[]
        {
            1L,
            "LocalPlayer",
            "",
            0L,
            null,
            1L,
            null,
            null,
            null,
            0.0,
            null,
            true,
            0,
            0,
            true,
            null,
            DefaultCharacterMasterId,
            false,
            true,
            DefaultNameBaseColorMasterId,
            DefaultIconFrameMasterId,
            DefaultHomeSkinMasterId
        };

        var homeDisplayPreference = new object?[]
        {
            1L,
            DefaultCharacterBaseMasterId,
            null,
            DefaultCharacterBaseMasterId,
            DefaultCharacterBaseMasterId,
            DefaultCharacterBaseMasterId,
            DefaultCostumeMasterId,
            null,
            DefaultCostumeMasterId,
            DefaultCostumeMasterId,
            DefaultCostumeMasterId,
            DefaultCharacterMasterId,
            false,
            0,
            DefaultCharacterBaseMasterId,
            DefaultCostumeMasterId
        };

        var character = new object?[]
        {
            DefaultCharacterId,
            DefaultCharacterMasterId,
            1,
            0,
            0,
            0,
            DefaultCharacterBaseId,
            1,
            0,
            0,
            false,
            null,
            0,
            0,
            false
        };

        var characterBase = new object?[]
        {
            DefaultCharacterBaseId,
            DefaultCharacterBaseMasterId,
            1,
            0,
            DefaultCostumeMasterId,
            0,
            DefaultCharacterId,
            false
        };

        var costume = new object?[]
        {
            DefaultCostumeId,
            DefaultCostumeMasterId
        };

        var homeSkin = new object?[]
        {
            1L,
            new long[] { DefaultHomeSkinMasterId }
        };

        var homeBgm = new object?[]
        {
            DefaultHomeBgmMasterId,
            1,
            DefaultHomeBgmDetailMasterId
        };

        var currency = new object?[]
        {
            1L,
            10000,
            0,
            0
        };

        return new object?[]
        {
            DataObject(0, user),
            DataObject(1, userProfile),
            DataObject(3, homeDisplayPreference),
            DataObject(4, character),
            DataObject(5, characterBase),
            DataObject(43, costume),
            DataObject(128, currency),
            DataObject(161, homeBgm),
            DataObject(189, homeSkin)
        };
    }

    static object?[] DataObject(int unionKey, object?[] value)
    {
        return new object?[] { unionKey, value };
    }

    static long? ConvertToLong(object? value)
    {
        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            ushort ushortValue => ushortValue,
            uint uintValue => uintValue,
            ulong ulongValue when ulongValue <= long.MaxValue => (long)ulongValue,
            string text when long.TryParse(text, out var longValue) => longValue,
            _ => null
        };
    }

    static bool? ConvertToBool(object? value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var boolValue) => boolValue,
            _ => null
        };
    }

    static long ReadLiveMasterId(object? requestBody)
    {
        return requestBody is object?[] values && values.Length > 1
            ? ConvertToLong(values[1]) ?? 0
            : 0;
    }

    static bool? ReadLiveStartFlag(object? requestBody)
    {
        return requestBody is object?[] values && values.Length > 7
            ? ConvertToBool(values[7])
            : null;
    }


}

