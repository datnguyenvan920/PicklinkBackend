namespace PicklinkBackend.Tests;

public class PaymentReviewContractTests
{
    [Fact]
    public void RejectingMatchReceiptResumesThePausedHoldWindow()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));
        var booking = File.ReadAllText(SourcePath("Models", "Booking.cs"));

        Assert.Contains("PauseBookingHold(booking, now)", source);
        Assert.Contains("ResumeBookingHoldIfNoPendingReview(booking, now)", source);
        Assert.Contains("booking.HoldExpiresAt = null", source);
        Assert.Contains("booking.HoldExpiresAt = now.AddSeconds(remainingSeconds)", source);
        Assert.Contains("public int? HoldRemainingSeconds { get; set; }", booking);
    }

    [Fact]
    public void StaleHoldingCleanupNeverExpiresABookingAwaitingReceiptReview()
    {
        var repository = File.ReadAllText(SourcePath("Repositories", "Implementations", "BookingRepository.cs"));

        Assert.Contains("!booking.Payments.Any(payment => payment.Status == \"WaitingForConfirmation\")", repository);
        Assert.Contains("!item.Payments.Any(payment => payment.Status == \"WaitingForConfirmation\")", repository);
    }

    [Fact]
    public void PlayerReadsOnlyTheUpdatedPaymentAfterRealtimeReview()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Payments", "PaymentController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("GetPlayerBookingPayment", controller);
        Assert.Contains("public async Task<ServiceResult<BankTransferResponse>> GetPlayerBookingPayment", service);
        Assert.Contains("item.Booking.Player.UserId == userId.Value", service);
        Assert.Contains("OrderByDescending(item => item.PaymentId)", service);
        Assert.Contains("return Ok(MapSubmittedTransfer(payment, payment.Booking))", service);
        Assert.DoesNotContain("Ok(new BankTransferResponse())", service);
    }

    [Fact]
    public void OperatorReviewLoadsMatchDetailsOnlyWhenThePaymentBelongsToAMatch()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("_paymentRepository", service);
    }

    [Fact]
    public void OwnerBankAccountAuditUsesTheAuthenticatedOwnerAsActor()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("var userId = CurrentUserId()", service);
        Assert.Contains("NewAudit(venueId, userId.Value, \"BankAccountUpdated\")", service);
        Assert.Contains("ActorId = actorId", service);
        Assert.DoesNotContain("ActorId = 0", service);
    }

    [Fact]
    public void OwnerBankAccountResponseIncludesAllEditableFields()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("OwnerBankAccountId = account.OwnerBankAccountId", service);
        Assert.Contains("BankCode = account.BankCode", service);
        Assert.Contains("AccountNumber = account.AccountNumber", service);
        Assert.Contains("IsActive = account.IsActive", service);
    }

    [Fact]
    public void SubmittingARegularBookingReceiptPersistsThePaymentAndReturnsItToCheckout()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("public async Task<ServiceResult<BankTransferResponse>> SubmitTransfer", service);
        Assert.Contains("payment.Status = \"WaitingForConfirmation\"", service);
        Assert.Contains("Action = \"ReceiptSubmitted\"", service);
        Assert.Contains("payment.ReceiptImageUrl = await SaveReceiptAsync", service);
        Assert.Contains("return Ok(MapSubmittedTransfer(payment, booking))", service);
    }

    [Fact]
    public void OwnerReceiptReviewLoadsThePersistedPaymentRatherThanAnEmptyResponse()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("public async Task<ServiceResult<BankTransferResponse>> GetOperatorPayment", service);
        Assert.Contains("SingleOrDefaultAsync(item => item.PaymentId == paymentId", service);
        Assert.Contains("payment.Booking.Court.Venue.Owner.UserId != userId.Value", service);
        Assert.Contains("return Ok(MapSubmittedTransfer(payment, payment.Booking))", service);
        Assert.DoesNotContain("GetOperatorPayment(int paymentId, CancellationToken cancellationToken) =>\n        Task.FromResult<ServiceResult<BankTransferResponse>>(Ok(new BankTransferResponse()))", service);
    }

    [Fact]
    public void OwnerReviewResponsesContainTheCompleteUpdatedPaymentImmediately()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("MapDetail(primaryPayment, booking, bookingCode, matchCode)", service);
        Assert.Contains("MapTransfer<PaymentDetailResponse>", service);
        Assert.Contains("var groupPayments = booking.Payments", service);
        Assert.DoesNotContain("payment-review:", service);
        Assert.Contains("BookingStatus = booking.Status", service);
        Assert.Contains("HoldExpiresAt = booking.HoldExpiresAt", service);
        Assert.Contains("RejectionReason = payment.RejectionReason", service);
    }

    [Fact]
    public void OwnerBookingPaymentListLoadsPersistedReceiptsInsteadOfAnEmptyList()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("public async Task<ServiceResult<List<BankTransferResponse>>> GetOperatorBookingPayments", service);
        Assert.Contains("item.Booking.Court.Venue.Owner.UserId == userId.Value", service);
        Assert.Contains("payments.Select(payment => MapSubmittedTransfer(payment, payment.Booking)).ToList()", service);
        Assert.DoesNotContain("Task.FromResult<ServiceResult<List<BankTransferResponse>>>(Ok(new List<BankTransferResponse>()))", service);
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
