namespace PicklinkBackend.Tests.ApiContracts;

public class SePayWebhookContractTests
{
    [Fact]
    public void WebhookChecksAccountCodeExactAmountAndBookingStateBeforeConfirmation()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "SePayWebhookService.cs"));

        Assert.Contains("item.BankAccountNumber == request.AccountNumber.Trim()", source);
        Assert.Contains("paymentCodes.Contains(item.TransferContent)", source);
        Assert.DoesNotContain("content.Contains(item.TransferContent)", source);
        Assert.Contains("expectedAmount != request.TransferAmount", source);
        Assert.Contains(".OrderBy(item => item)", source);
        Assert.DoesNotContain(".Order()", source);
        Assert.Contains("item.Booking.Status != \"Holding\"", source);
        Assert.Contains("payment.Status = \"Paid\"", source);
        Assert.Contains("booking.Status = \"Confirmed\"", source);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}


