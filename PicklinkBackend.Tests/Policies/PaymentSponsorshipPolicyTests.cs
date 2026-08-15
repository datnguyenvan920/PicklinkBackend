namespace PicklinkBackend.Tests.Policies;

public class PaymentSponsorshipPolicyTests
{
    [Fact]
    public void ProxyPaymentRequiresOwnerOptInAndAnExclusiveClaim()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));
        var previewStart = source.IndexOf(" PreviewBatchTransfer(", StringComparison.Ordinal);
        var submitStart = source.IndexOf(" SubmitBatchTransfer(", previewStart, StringComparison.Ordinal);
        var submitEnd = source.IndexOf(" SubmitTransfer(", submitStart, StringComparison.Ordinal);
        var preview = source[previewStart..submitStart];
        var submit = source[submitStart..submitEnd];

        Assert.Contains("!item.AllowPaymentByOthers", preview);
        Assert.Contains("payment.ClaimedByPlayerId = currentPlayer.PlayerId", preview);
        Assert.Contains("payment.ClaimExpiresAt = claimExpiresAt", preview);
        Assert.Contains("item.Status != \"Pending\"", submit);
        Assert.Contains("!HasActivePaymentClaim(item, now)", submit);
        Assert.Contains("item.ClaimedByPlayerId != currentPlayer.PlayerId", submit);
    }

    [Fact]
    public void PlayerControlsWhetherTheirOwnShareCanBePaidBySomeoneElse()
    {
        var payment = File.ReadAllText(SourcePath("Models", "Payment.cs"));
        var controller = File.ReadAllText(SourcePath("Controllers", "Payments", "PaymentController.cs"));

        Assert.Contains("public bool AllowPaymentByOthers", payment);
        Assert.Contains("public int? ClaimedByPlayerId", payment);
        Assert.Contains("public DateTime? ClaimExpiresAt", payment);
        Assert.Contains("bookings/{bookingId:int}/sponsorship", controller);
    }

    private static string SourcePath(params string[] segments)
    {
        var fileName = segments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. segments]);
                if (File.Exists(candidate)) return candidate;

                var found = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) return found;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', segments)}.");
    }
}
