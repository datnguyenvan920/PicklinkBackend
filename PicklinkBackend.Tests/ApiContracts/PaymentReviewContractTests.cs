namespace PicklinkBackend.Tests;

public class PaymentReviewContractTests
{
    [Fact]
    public void RegularBookingRejectedReceiptResumesOnlyTheSavedRemainingHoldWindow()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));
        var booking = File.ReadAllText(SourcePath("Models", "Booking.cs"));

        Assert.Contains("PauseBookingHold(booking, now)", source);
        Assert.Contains("ResumeBookingHoldIfNoPendingReview(booking, now)", source);
        Assert.Contains("(int)Math.Ceiling((booking.HoldExpiresAt.Value - now).TotalSeconds)", source);
        Assert.Contains("booking.HoldExpiresAt = null", source);
        Assert.Contains("now.AddSeconds(Math.Max(1, booking.HoldRemainingSeconds.Value))", source);
        Assert.DoesNotContain("booking.HoldRemainingSeconds ?? configuredSeconds", source);
        Assert.Contains("public int? HoldRemainingSeconds { get; set; }", booking);
    }

    [Fact]
    public void MatchReceiptReviewKeepsTheOriginalDeadlineRunning()
    {
        var payment = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));
        var repository = File.ReadAllText(SourcePath("Repositories", "Implementations", "BookingRepository.cs"));
        var expiration = File.ReadAllText(SourcePath("Services", "Bookings", "BookingHoldExpirationService.cs"));
        var match = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var migration = File.ReadAllText(SourcePath("Migrations", "20260822090000_KeepMatchPaymentDeadlineRunning.cs"));
        var batchSubmission = SourceBetween(
            payment,
            "public async Task<ServiceResult<BatchPaymentResponse>> SubmitBatchTransfer(",
            "public async Task<ServiceResult<BankTransferResponse>> SubmitTransfer(");

        Assert.DoesNotContain("PauseBookingHold", batchSubmission);
        Assert.Contains("!booking.HoldExpiresAt.HasValue", batchSubmission);
        Assert.Contains("booking.HoldRemainingSeconds = null", batchSubmission);
        Assert.Contains("booking.MatchId.HasValue", repository);
        Assert.Contains("booking.Payments.Any(payment => payment.Status == \"Pending\")", repository);
        Assert.Contains("!booking.Payments.Any(payment => payment.Status == \"WaitingForConfirmation\")", repository);
        Assert.DoesNotContain("!booking.HoldExpiresAt.HasValue", repository);
        Assert.DoesNotContain(": firstBooking?.HoldRemainingSeconds", match);
        Assert.Contains("DATEADD(MINUTE, 20, [createdAt])", migration);
        Assert.Contains("[holdRemainingSeconds] = NULL", migration);
        Assert.Contains("MatchPaymentDeadlineDecision.ExpireAndRefund", expiration);
        Assert.Contains("payment.Status = nextStatus", expiration);
        Assert.Contains("\"RefundPending\"", expiration);
        Assert.Contains("MatchRoomLifecyclePolicy.RoomStatusFor", expiration);
        Assert.DoesNotContain("RemoveUnpaidMatchParticipantsAsync", expiration);
    }

    [Fact]
    public void MatchPaymentUsesOneTwentyMinuteDeadlineWithoutARescueWindow()
    {
        var booking = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Open.cs"));
        var expiration = File.ReadAllText(SourcePath("Services", "Bookings", "BookingHoldExpirationService.cs"));

        Assert.Contains("GetValue(\"Match:PaymentMinutes\", 20)", booking);
        Assert.DoesNotContain("TimeSpan.FromMinutes(10)", expiration);
        Assert.DoesNotContain("MatchPaymentDeadlineDecision.StartRescue", expiration);
        Assert.DoesNotContain("Mở thêm 10 phút", expiration);
        Assert.Contains("sau thời hạn 20 phút", expiration);
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

    [Fact]
    public void RefundRequiresOwnerProofAndBecomesFinalOnlyAfterTheActualSenderConfirmsReceipt()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Payments", "PaymentController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));
        var dispute = SourceBetween(
            service,
            "public async Task<ServiceResult<List<BankTransferResponse>>> DisputeRefund(",
            "public async Task<ServiceResult<List<BankTransferResponse>>> ConfirmMatchRefundReceived(");

        Assert.Contains("operator/{paymentId:int}/refund-sent", controller);
        Assert.Contains("{paymentId:int}/refund/proof-file", controller);
        Assert.Contains("{paymentId:int}/refund/dispute", controller);
        Assert.Contains("{paymentId:int}/refund/confirm", controller);
        Assert.Contains("item.ClaimedByPlayerId ?? item.PayerId", service);
        Assert.Contains(@"Action = isUpdate ? ""OwnerUpdatedRefundProof"" : ""OwnerMarkedRefundSent""", service);
        Assert.Contains(@"Action = ""PlayerDisputedRefund""", service);
        Assert.Contains(@"Action = ""PlayerConfirmedRefund""", service);
        Assert.Contains(@"LinkTo: $""/notifications?refundPaymentId={selectedPayment.PaymentId}""", service);
        Assert.Contains("payment.RefundDisputeStatus = \"Open\"", service);
        Assert.Contains("item.RefundDisputeStatus == \"Open\"", service);
        Assert.Contains("private-uploads", service);
        Assert.Contains(@"payment.Status = ""Refunded""", service);
        Assert.Contains(@"if (booking.MatchId.HasValue) _matchRealtime.Publish(booking.MatchId.Value, ""RefundConfirmed"")", service);
        Assert.Contains(".Select(item => new { item.PlayerId, item.User.Username })", dispute);
        Assert.DoesNotContain("player.User.Username", dispute);
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

    private static string SourceBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not find source range {startMarker} -> {endMarker}.");
        return source[start..end];
    }
}
