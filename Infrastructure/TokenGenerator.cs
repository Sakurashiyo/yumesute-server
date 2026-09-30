using System.Security.Cryptography;

static class TokenGenerator
{
    public static string NewToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }
}

