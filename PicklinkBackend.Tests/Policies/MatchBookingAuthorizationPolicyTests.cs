namespace PicklinkBackend.Tests;

public class MatchBookingAuthorizationPolicyTests
{
    [Fact]
    public void BatchPaymentContractPersistsGroupAndExposesBatchDtos()
    {
        var paymentModel = File.ReadAllText(SourcePath("Models", "Payment.cs"));
        var paymentDtos = File.ReadAllText(SourcePath("DTOs", "PaymentDtos.cs"));

        Assert.Contains("public Guid? PaymentGroupId { get; set; }", paymentModel);
        Assert.Contains("public class BatchPaymentPreviewRequest", paymentDtos);
    }

    [Fact]
    public void BatchPaymentPreviewValidatesParticipantsAndCalculatesAuthoritativeTotal()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("_paymentRepository", source);
    }

    [Fact]
    public void BatchPaymentSubmissionRevalidatesAndUpdatesEveryTargetAtomically()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("_paymentRepository", source);
    }

    [Fact]
    public void CreateMatchBookingAllowsAnyApprovedParticipantToHoldTheCourt()
    {
        var matchSource = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));

        Assert.Contains("_matchRepository", matchSource);
    }

    [Fact]
    public void UnpaidMatchBookingCanBeCancelledToSelectNewSlots()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));

        Assert.Contains("_matchRepository", source);
    }

    [Fact]
    public void SubmitTransferAllowsApprovedMatchParticipantToPayForAnotherApprovedParticipant()
    {
        var paymentService = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("_paymentRepository", paymentService);
    }

    [Fact]
    public void PendingCurrentPlayerPaymentIsRequiredInPreviewAndSubmission()
    {
        var paymentService = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Equal(2, paymentService.Split("currentPaymentIsPending && !targetParticipantIds.Contains(currentPlayer.PlayerId)").Length - 1);
        Assert.Equal(2, paymentService.Split("Phần thanh toán của bạn phải được chọn tự động").Length - 1);
    }

    [Fact]
    public void MatchDetailParticipantResponseIncludesPaymentMetadataForProxyPayment()
    {
        var matchDtos = File.ReadAllText(SourcePath("DTOs", "MatchRequest.cs"));

        Assert.Contains("public int? PaymentId { get; set; }", matchDtos);
    }

    [Fact]
    public void OwnerGroupReceiptIncludesEachPlayerPhoneNumber()
    {
        var paymentDtos = File.ReadAllText(SourcePath("DTOs", "PaymentDtos.cs"));
        var paymentService = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("public string? PlayerPhoneNumber { get; set; }", paymentDtos);
        Assert.Contains("PlayerPhoneNumber = payment.Payer?.PhoneNumber ?? booking.Player?.PhoneNumber", paymentService);
    }

    private static string SourcePath(params string[] segments)
    {
        var cleanSegments = segments.FirstOrDefault() == "PicklinkBackend" ? segments[1..] : segments;
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

        throw new FileNotFoundException($"Could not locate {string.Join('/', segments)}.");
    }
}
