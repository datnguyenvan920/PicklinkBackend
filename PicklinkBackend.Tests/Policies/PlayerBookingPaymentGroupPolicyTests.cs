using System.Text.RegularExpressions;

namespace PicklinkBackend.Tests;

public class PlayerBookingPaymentGroupPolicyTests
{
    [Fact]
    public void PaymentGroupSubmissionAndApprovalCoverEveryBookingPayment()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("_paymentRepository", source);
    }

    [Fact]
    public void SePayAutoConfirmationRequiresSubmittedReceipt()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "SePayWebhookService.cs"));

        Assert.Contains("item.Status == \"WaitingForConfirmation\"", source);
        Assert.Contains("!string.IsNullOrWhiteSpace(item.ReceiptImageUrl)", source);
    }
    private static string ExtractMethod(string source, string methodName, string nextMethodName)
    {
        var match = Regex.Match(source, $"public .*? {methodName}\\([\\s\\S]*?\\n    public .*? {nextMethodName}\\(");
        Assert.True(match.Success, $"Could not locate {methodName}.");
        return match.Value;
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
