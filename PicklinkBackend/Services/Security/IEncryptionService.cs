namespace PicklinkBackend.Services.Security;

/// <summary>
/// Symmetric encryption for secrets that the backend has to be able to read back
/// (third-party API tokens), as opposed to passwords, which stay hashed.
/// </summary>
public interface IEncryptionService
{
    /// <summary>Encrypts <paramref name="plainText"/> and returns the Base64 envelope stored in the database.</summary>
    string Encrypt(string plainText);

    /// <summary>Reverses <see cref="Encrypt"/>. Throws when the envelope is malformed or was written under a different key.</summary>
    string Decrypt(string cipherText);
}
