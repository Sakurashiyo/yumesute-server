sealed record AuthenticatePayload(
    string LoginToken,
    string GameVersion,
    string ApkHash,
    string ApkApplicationSignature,
    string ApplicationVersion);

