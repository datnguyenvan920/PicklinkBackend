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
        var cleanSegments = relativeSegments.FirstOrDefault() == "PicklinkBackend" ? relativeSegments[1..] : relativeSegments;
        var fileName = cleanSegments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. cleanSegments]);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;

                var foundFile = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundFile is not null) return foundFile;

                var foundDir = Directory.GetDirectories(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundDir is not null) return foundDir;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}


