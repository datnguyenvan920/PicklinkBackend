using System.Security.Cryptography;
using System.Text;

namespace PicklinkBackend.Services.Security.Implementations;

/// <summary>
/// AES-256-GCM. Every ciphertext carries its own random nonce and authentication tag, so the
/// same token encrypted twice yields different envelopes and a tampered row fails to decrypt
/// instead of silently returning garbage.
/// </summary>
public sealed class AesEncryptionService : IEncryptionService
{
    private const byte EnvelopeVersion = 1;
    private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagSize = 16;   // AesGcm.TagByteSizes.MaxSize
    private const int MinimumConfiguredKeyLength = 16;

    private readonly byte[] _key;

    public AesEncryptionService(IConfiguration configuration)
    {
        var configuredKey = configuration["Security:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(configuredKey) || configuredKey.Trim().Length < MinimumConfiguredKeyLength)
            throw new InvalidOperationException(
                $"Security:EncryptionKey is not configured or is shorter than {MinimumConfiguredKeyLength} characters.");

        // The configured value is a passphrase of any length; SHA-256 folds it into the
        // exact 256-bit key AES-GCM requires.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey.Trim()));
    }

    public string Encrypt(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        if (plainText.Length == 0) return string.Empty;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var envelope = new byte[1 + NonceSize + TagSize + plainBytes.Length];
        envelope[0] = EnvelopeVersion;

        var nonce = envelope.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(
            nonce,
            plainBytes,
            envelope.AsSpan(1 + NonceSize + TagSize, plainBytes.Length),
            envelope.AsSpan(1 + NonceSize, TagSize));

        return Convert.ToBase64String(envelope);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        if (cipherText.Length == 0) return string.Empty;

        byte[] envelope;
        try
        {
            envelope = Convert.FromBase64String(cipherText);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("Encrypted value is not a valid Base64 envelope.", exception);
        }

        if (envelope.Length < 1 + NonceSize + TagSize || envelope[0] != EnvelopeVersion)
            throw new CryptographicException("Encrypted value is not a recognised AES-GCM envelope.");

        var cipherLength = envelope.Length - 1 - NonceSize - TagSize;
        var plainBytes = new byte[cipherLength];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            envelope.AsSpan(1, NonceSize),
            envelope.AsSpan(1 + NonceSize + TagSize, cipherLength),
            envelope.AsSpan(1 + NonceSize, TagSize),
            plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
