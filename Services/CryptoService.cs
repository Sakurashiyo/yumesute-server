using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class CryptoService
{
    public static string Sha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public static string RandomToken(int bytes = 32)
    {
        return Base64Url(RandomNumberGenerator.GetBytes(bytes));
    }

    public static string RandomNumericCode(int digits = 6)
    {
        if (digits <= 0 || digits > 18) throw new ArgumentOutOfRangeException(nameof(digits));
        var min = (long)Math.Pow(10, digits - 1);
        var max = (long)Math.Pow(10, digits);
        return RandomInt64(min, max).ToString();
    }

    public static string RandomJwtToken(string secret, int ttlSeconds)
    {
        return SignJwt(
            new Dictionary<string, object?>
            {
                ["jti"] = RandomToken(24),
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            },
            secret,
            ttlSeconds);
    }

    public static string SignToken(IReadOnlyDictionary<string, object?> payload, string secret, int ttlSeconds)
    {
        return SignJwt(payload, secret, ttlSeconds);
    }

    static string SignJwt(IReadOnlyDictionary<string, object?> payload, string secret, int ttlSeconds)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var body = new Dictionary<string, object?>(payload)
        {
            ["nbf"] = now,
            ["exp"] = now + ttlSeconds,
            ["iat"] = now,
            ["iss"] = "sirius.kms3.com",
            ["aud"] = "sirius"
        };
        var header = new Dictionary<string, object?>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };
        var encodedHeader = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header)));
        var encodedBody = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body)));
        var signingInput = $"{encodedHeader}.{encodedBody}";
        var sig = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signingInput)));
        return $"{signingInput}.{sig}";
    }

    static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    static long RandomInt64(long minInclusive, long maxExclusive)
    {
        if (minInclusive >= maxExclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));

        var range = (ulong)(maxExclusive - minInclusive);
        var limit = ulong.MaxValue - (ulong.MaxValue % range);
        Span<byte> bytes = stackalloc byte[8];
        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            var value = BitConverter.ToUInt64(bytes);
            if (value < limit)
            {
                return minInclusive + (long)(value % range);
            }
        }
    }
}

