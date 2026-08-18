using PicklinkBackend.DTOs;

namespace PicklinkBackend.Tests;

public class OwnerSePayTokenContractTests
{
    [Fact]
    public void BankAccountResponseCarriesOnlyAMaskOfTheToken()
    {
        var properties = typeof(OwnerBankAccountResponse).GetProperties().Select(item => item.Name).ToArray();

        Assert.Contains(nameof(OwnerBankAccountResponse.HasSePayApiToken), properties);
        Assert.Contains(nameof(OwnerBankAccountResponse.MaskedSePayApiToken), properties);
        Assert.DoesNotContain("SePayApiToken", properties);
    }

    [Fact]
    public void OwnerTokenIsEncryptedBeforeItReachesTheDatabase()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("_encryptionService.Encrypt(token)", service);
        Assert.Contains("MaskedSePayApiToken = MaskStoredToken(account.SePayApiToken)", service);
        // A null token means "unchanged", so a plain bank-details save must not clear the token.
        Assert.Contains("if (request.SePayApiToken is not null)", service);
        Assert.DoesNotContain("MaskedSePayApiToken = account.SePayApiToken", service);
        Assert.DoesNotContain("account.SePayApiToken = token;", service);
    }

    [Fact]
    public void ReconciliationCallsSePayWithTheOwnersOwnDecryptedToken()
    {
        var reconciliation = File.ReadAllText(SourcePath("Services", "Payments", "SePayReconciliationService.cs"));
        var queryClient = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "SePayTransactionQueryClient.cs"));

        Assert.Contains("ResolveOwnerApiTokenAsync(transferContent, cancellationToken)", reconciliation);
        Assert.Contains("_encryptionService.Decrypt(encryptedToken)", reconciliation);
        Assert.Contains("payment.Booking.Court.Venue.Owner.BankAccounts", reconciliation);
        Assert.Contains("FindIncomingTransactionAsync(transferContent, ownerToken, cancellationToken)", reconciliation);
        // Owner token first, platform token only as a fallback.
        Assert.Contains("string.IsNullOrWhiteSpace(apiToken)", queryClient);
        Assert.Contains("_configuration[\"SePay:ApiToken\"]", queryClient);
        Assert.Contains("new AuthenticationHeaderValue(\"Bearer\", effectiveToken)", queryClient);
        Assert.Contains("transactions", queryClient);
    }

    [Fact]
    public void TokenColumnIsMappedAndBackedByAMigration()
    {
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var model = File.ReadAllText(SourcePath("Models", "OwnerBankAccount.cs"));
        var migrationsDirectory = SourcePath("Migrations");
        var migration = Directory
            .GetFiles(migrationsDirectory, "*_AddSePayApiTokenToOwnerBankAccount.cs")
            .Single();

        Assert.Contains("public string? SePayApiToken { get; set; }", model);
        Assert.Contains(
            "entity.Property(e => e.SePayApiToken).HasMaxLength(500).HasColumnName(\"sePayApiToken\");",
            dbContext);
        Assert.Contains("[OWNER_BANK_ACCOUNT] ADD [sePayApiToken] nvarchar(500) NULL", File.ReadAllText(migration));
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. relativeSegments]);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
