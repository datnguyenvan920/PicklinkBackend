using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using PicklinkBackend.Services.Security;
using PicklinkBackend.Services.Security.Implementations;

namespace PicklinkBackend.Tests.Services;

public class AesEncryptionServiceTests
{
    private const string SePayToken = "BKDPWIO1H01ZLGWYDYSOFAJ448F6IM5DYTNBU06ECRZQC5MZKSWDS2BKJVJTAMEP";

    [Fact]
    public void Encrypt_HidesTheTokenAndDecryptRestoresIt()
    {
        var service = Build("picklink_owner_secret_encryption_key_test");

        var cipherText = service.Encrypt(SePayToken);

        Assert.DoesNotContain(SePayToken, cipherText, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(SePayToken, cipherText);
        Assert.Equal(SePayToken, service.Decrypt(cipherText));
    }

    [Fact]
    public void Encrypt_UsesAFreshNonceSoTheSameTokenNeverRepeatsItself()
    {
        var service = Build("picklink_owner_secret_encryption_key_test");

        Assert.NotEqual(service.Encrypt(SePayToken), service.Encrypt(SePayToken));
    }

    [Fact]
    public void EncryptedTokenFitsTheNvarchar500Column()
    {
        var service = Build("picklink_owner_secret_encryption_key_test");

        // 200 is the MaxLength the DTO accepts; the envelope around it still has to fit the column.
        Assert.True(service.Encrypt(new string('T', 200)).Length <= 500);
    }

    [Fact]
    public void Decrypt_RejectsTamperedEnvelopesAndForeignKeys()
    {
        var service = Build("picklink_owner_secret_encryption_key_test");
        var cipherText = service.Encrypt(SePayToken);

        // ThrowsAny, because a tampered payload surfaces as AuthenticationTagMismatchException --
        // a CryptographicException subclass, which is what the callers catch.
        var envelope = Convert.FromBase64String(cipherText);
        envelope[^1] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => service.Decrypt(Convert.ToBase64String(envelope)));

        Assert.ThrowsAny<CryptographicException>(() => service.Decrypt("not-base64!!"));
        Assert.ThrowsAny<CryptographicException>(() => service.Decrypt(Convert.ToBase64String([1, 2, 3])));

        var otherService = Build("a_completely_different_encryption_key_2026");
        Assert.ThrowsAny<CryptographicException>(() => otherService.Decrypt(cipherText));
    }

    [Fact]
    public void Constructor_RefusesToStartWithoutAUsableKey()
    {
        Assert.Throws<InvalidOperationException>(() => Build(null));
        Assert.Throws<InvalidOperationException>(() => Build("   "));
        Assert.Throws<InvalidOperationException>(() => Build("too-short"));
    }

    [Fact]
    public void Mask_ShowsOnlyAShortPrefix()
    {
        Assert.Equal("BKDPWIO1****", SecretMask.Mask(SePayToken));
        Assert.Equal("****", SecretMask.Mask("A"));
        Assert.Equal(string.Empty, SecretMask.Mask(null));
        Assert.DoesNotContain(SePayToken[8..], SecretMask.Mask(SePayToken), StringComparison.Ordinal);
    }

    private static IEncryptionService Build(string? key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:EncryptionKey"] = key })
            .Build();
        return new AesEncryptionService(configuration);
    }
}
