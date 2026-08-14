namespace PicklinkBackend.Services.Security;

/// <summary>
/// Builds the preview shown in place of a stored secret, so a UI can prove which token is
/// configured without the API ever handing back a usable one.
/// </summary>
public static class SecretMask
{
    private const int MaxVisibleCharacters = 8;
    private const string Suffix = "****";

    /// <summary>
    /// Keeps at most the first 8 characters -- and never more than half of a short secret --
    /// then replaces the remainder with asterisks, e.g. "BKDPWIO1****".
    /// </summary>
    public static string Mask(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        var visible = Math.Min(MaxVisibleCharacters, secret.Length / 2);
        return secret[..visible] + Suffix;
    }
}
