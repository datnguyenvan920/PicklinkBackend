namespace PicklinkBackend.Tests;

public class MatchBookingAuthorizationPolicyTests
{
    [Fact]
    public void BatchPaymentContractPersistsGroupAndExposesBatchDtos()
    {
        var paymentModel = File.ReadAllText(SourcePath("Models", "Payment.cs"));
        var paymentDtos = File.ReadAllText(PaymentDtosSourcePath());
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var snapshot = File.ReadAllText(SourcePath("Migrations", "ApplicationDbContextModelSnapshot.cs"));
        var migration = File.ReadAllText(SourcePath("Migrations", "20260705002333_AddPaymentGroupId.cs"));

        Assert.Contains("public Guid? PaymentGroupId { get; set; }", paymentModel);
        Assert.Contains("public class BatchPaymentPreviewRequest", paymentDtos);
        Assert.Contains("public List<int> PayerIds { get; set; } = [];", paymentDtos);
        Assert.Contains("public class BatchPaymentPreviewResponse", paymentDtos);
        Assert.Contains("public decimal TotalAmount { get; set; }", paymentDtos);
        Assert.Contains("public string QrImageUrl { get; set; }", paymentDtos);
        Assert.Contains("public class SubmitBatchPaymentReceiptRequest", paymentDtos);
        Assert.Contains("public class BatchPaymentResponse", paymentDtos);
        Assert.Contains("public Guid? PaymentGroupId { get; set; }", paymentDtos);
        Assert.Contains("public int GroupPaymentCount { get; set; }", paymentDtos);
        Assert.Contains("public decimal GroupTotalAmount { get; set; }", paymentDtos);
        Assert.Contains("entity.HasIndex(e => e.PaymentGroupId", dbContext);
        Assert.Contains("entity.Property(e => e.PaymentGroupId)", dbContext);
        Assert.Contains("b.Property<Guid?>(\"PaymentGroupId\")", snapshot);
        Assert.Contains("name: \"paymentGroupId\"", migration);
        Assert.Contains("name: \"IX_PAYMENT_paymentGroupId\"", migration);
    }

    [Fact]
    public void BatchPaymentPreviewValidatesParticipantsAndCalculatesAuthoritativeTotal()
    {
        var source = File.ReadAllText(PaymentControllerSourcePath());
        var method = ExtractMethod(
            source,
            "public async Task<ServiceResult<BatchPaymentPreviewResponse>> PreviewBatchTransfer");

        Assert.Contains("request.PayerIds.Count == 0", method);
        Assert.Contains("request.PayerIds.Distinct().Count() != request.PayerIds.Count", method);
        Assert.Contains("booking.Match is null", method);
        Assert.Contains("currentParticipantIsApproved", method);
        Assert.Contains("targetParticipantIds.SetEquals", method);
        Assert.Contains("payments.Any(item => item.Status != \"Pending\")", method);
        Assert.Contains("payments.Sum(item => item.Amount)", method);
        Assert.Contains("BuildBatchVietQrUrl", method);
        Assert.DoesNotContain("request.TotalAmount", method);
        Assert.DoesNotContain("request.QrImageUrl", method);
    }

    [Fact]
    public void BatchPaymentSubmissionRevalidatesAndUpdatesEveryTargetAtomically()
    {
        var source = File.ReadAllText(PaymentControllerSourcePath());
        var method = ExtractMethod(
            source,
            "public async Task<ServiceResult<BatchPaymentResponse>> SubmitBatchTransfer");

        Assert.Contains("IsolationLevel.Serializable", method);
        Assert.Contains("booking-payment:", method);
        Assert.Contains("match-roster:", method);
        Assert.Contains("request.PayerIds.Count == 0", method);
        Assert.Contains("request.PayerIds.Distinct().Count() != request.PayerIds.Count", method);
        Assert.Contains("payments.Any(item => item.Status != \"Pending\")", method);
        Assert.Contains("var paymentGroupId = Guid.NewGuid()", method);
        Assert.Contains("foreach (var payment in payments)", method);
        Assert.Contains("payment.PaymentGroupId = paymentGroupId", method);
        Assert.Contains("payment.ReceiptImageUrl = receiptUrl", method);
        Assert.Contains("payment.Status = \"WaitingForConfirmation\"", method);
        Assert.Contains("await transaction.CommitAsync", method);
    }

    [Fact]
    public void BatchPaymentTransferContentUsesABoundedDeterministicSelectionHash()
    {
        var source = File.ReadAllText(PaymentControllerSourcePath());

        Assert.Contains("SHA256.HashData", source);
        Assert.Contains("Convert.ToHexString", source);
        Assert.Contains("[..12]", source);
        Assert.DoesNotContain(
            "$\"{booking.BookingCode ?? $\"PL-{booking.BookingId}\"}-G-{string.Join(\"-\", payerIds.Order())}\"",
            source);
    }

    [Fact]
    public void OwnerReviewApprovesEveryPaymentInTheSelectedBatch()
    {
        var source = File.ReadAllText(PaymentControllerSourcePath());
        var method = ExtractMethod(
            source,
            "public async Task<ServiceResult<BankTransferResponse>> ApprovePayment");

        Assert.Contains("payment.PaymentGroupId", method);
        Assert.Contains("groupPayments", method);
        Assert.DoesNotContain("item.BookingId == payment.BookingId", method);
        Assert.Contains("item.PaymentGroupId == payment.PaymentGroupId", method);
        Assert.Contains("groupPayments.All", method);
        Assert.Contains("foreach (var groupPayment in groupPayments)", method);
        Assert.True(
            method.IndexOf("foreach (var groupPayment in groupPayments)", StringComparison.Ordinal)
            < method.IndexOf("FinalizeBookingAfterPaymentApproval", StringComparison.Ordinal),
            "The entire group must be marked paid before finalizing the booking.");
    }

    [Fact]
    public void OwnerReviewRejectsEveryPaymentInTheSelectedBatch()
    {
        var source = File.ReadAllText(PaymentControllerSourcePath());
        var method = ExtractMethod(
            source,
            "public async Task<ServiceResult<BankTransferResponse>> RejectPayment");

        Assert.Contains("payment.PaymentGroupId", method);
        Assert.Contains("groupPayments", method);
        Assert.DoesNotContain("item.BookingId == payment.BookingId", method);
        Assert.Contains("item.PaymentGroupId == payment.PaymentGroupId", method);
        Assert.Contains("groupPayments.All", method);
        Assert.Contains("foreach (var groupPayment in groupPayments)", method);
        Assert.Contains("var rejectionReason = request.Reason.Trim()", method);
        Assert.Contains("groupPayment.RejectionReason = rejectionReason", method);
    }

    [Fact]
    public void OwnerBookingListFiltersRegularBookingsByVietnamBookingCreatedDate()
    {
        var source = File.ReadAllText(SourcePath("Services", "Owner", "OwnerOperationQueryService.cs"));
        var method = ExtractMethod(
            source,
            "public async Task<OwnerOperationResult<PaginatedResponse<OwnerBookingResponse>>> ListBookingsAsync");

        Assert.Contains("query = query.Where(item => item.CreatedAt >= start);", method);
        Assert.Contains("query = query.Where(item => item.CreatedAt < end);", method);
        Assert.Contains("query.OrderByDescending(item => item.CreatedAt)", method);
        Assert.Contains("ToDateTime(TimeOnly.MinValue).AddHours(-7)", method);
    }

    [Fact]
    public void CreateMatchBookingAllowsAnyApprovedParticipantToHoldTheCourt()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());
        var method = ExtractMethod(
            source,
            "public async Task<ServiceResult<OpenMatchDetailResponse>> CreateMatchBooking");

        Assert.DoesNotContain("match.HostPlayerId != hostPlayerId", method);
        Assert.Contains("ApprovedParticipants(match)", method);
        Assert.Contains("participant.PlayerId == currentPlayerId.Value", method);
        Assert.Contains("PlayerId = currentPlayerId.Value", method);
    }

    [Fact]
    public void MatchBookingRequiresExplicitScheduleConflictConfirmation()
    {
        var matchSource = File.ReadAllText(MatchControllerSourcePath());
        var requestSource = File.ReadAllText(SourcePath("DTOs", "MatchRequest.cs"));
        var conflictServiceSource = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerScheduleConflictService.cs"));
        var method = ExtractMethod(
            matchSource,
            "public async Task<ServiceResult<OpenMatchDetailResponse>> CreateMatchBooking");

        Assert.Contains("public bool AllowScheduleConflicts { get; set; }", requestSource);
        Assert.Contains("LoadConflictDetailsAsync", conflictServiceSource);
        Assert.Contains("request.AllowScheduleConflicts", method);
        Assert.Contains("requiresScheduleConflictConfirmation = true", method);
        Assert.Contains("conflictingSlot = conflict", method);
        Assert.Contains("conflicts = scheduleConflicts.Distinct()", method);
    }
    [Fact]
    public void UnpaidMatchBookingCanBeCancelledToSelectNewSlots()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());
        var method = ExtractMethod(
            source,
            "public async Task<ServiceResult<OpenMatchDetailResponse>> CancelPendingMatchBooking");

        Assert.Contains("booking-payment:{bookingId.Value}", method);
        Assert.Contains("match.Status != \"BookingPending\"", method);
        Assert.Contains("booking.Status != \"Holding\"", method);
        Assert.Contains("booking.Payments.Any(item => item.Status != \"Pending\")", method);
        Assert.Contains("booking.Status = \"Cancelled\"", method);
        Assert.Contains("payment.Status = \"Cancelled\"", method);
        Assert.Contains("match.Status = \"ReadyToBook\"", method);
        Assert.Contains("await transaction.CommitAsync", method);
    }

    [Fact]
    public void SubmitTransferAllowsApprovedMatchParticipantToPayForAnotherApprovedParticipant()
    {
        var paymentController = File.ReadAllText(PaymentControllerSourcePath());
        var paymentDtos = File.ReadAllText(PaymentDtosSourcePath());
        var method = ExtractMethod(
            paymentController,
            "public async Task<ServiceResult<BankTransferResponse>> SubmitTransfer");

        Assert.Contains("public int? PayerId { get; set; }", paymentDtos);
        Assert.Contains("request.PayerId", method);
        Assert.Contains("targetPayerId", method);
        Assert.Contains("MatchParticipants", method);
        Assert.Contains("currentParticipantIsApproved", method);
        Assert.Contains("targetParticipantIsApproved", method);
        Assert.Contains("item.PayerId == targetPayerId", method);
        Assert.DoesNotContain("item.Payer.UserId == userId", method);
        Assert.True(
            method.IndexOf("currentParticipantIsApproved", StringComparison.Ordinal)
            < method.IndexOf("payment.Booking.Status != \"Holding\"", StringComparison.Ordinal),
            "Match participant authorization must run before returning booking status details.");
    }

    [Fact]
    public void MatchDetailParticipantResponseIncludesPaymentMetadataForProxyPayment()
    {
        var matchController = File.ReadAllText(MatchControllerSourcePath());
        var matchDtos = File.ReadAllText(MatchDtosSourcePath());
        var method = ExtractMethod(
            matchController,
            "private async Task<OpenMatchDetailResponse?> LoadOpenMatchResponseAsync");

        Assert.Contains("public int? PaymentId { get; set; }", matchDtos);
        Assert.Contains("public decimal? PaymentAmount { get; set; }", matchDtos);
        Assert.Contains("public string? QrImageUrl { get; set; }", matchDtos);
        Assert.Contains("public string? TransferContent { get; set; }", matchDtos);
        Assert.Contains("public string? PaymentRejectionReason { get; set; }", matchDtos);
        Assert.Contains("participantPayment", method);
        Assert.Contains("PaymentId = isApprovedParticipant ? participantPayment?.PaymentId : null", method);
        Assert.Contains("PaymentAmount = isApprovedParticipant ? participantPayment?.Amount : null", method);
        Assert.Contains("PaymentStatus = isApprovedParticipant ? participantPayment?.Status : null", method);
        Assert.Contains("QrImageUrl = isApprovedParticipant ? participantPayment?.QrImageUrl : null", method);
        Assert.Contains("TransferContent = isApprovedParticipant ? participantPayment?.TransferContent : null", method);
        Assert.Contains("PaymentRejectionReason = isApprovedParticipant ? participantPayment?.RejectionReason : null", method);
    }

    [Fact]
    public void MatchSplitPaymentsPreserveTheExactBookingTotal()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());
        var method = ExtractMethod(source, "private async Task CreateSplitPaymentsAsync");

        Assert.Contains("var totalAmount = Math.Round(EffectiveMatchTotal(booking), 0, MidpointRounding.AwayFromZero);", method);
        Assert.Contains("var baseAmount = decimal.Floor(totalAmount / participants.Count);", method);
        Assert.Contains("var remainder = (int)(totalAmount - baseAmount * participants.Count);", method);
        Assert.Contains("var amount = baseAmount + (index < remainder ? 1 : 0);", method);
        Assert.Contains("Amount = amount,", method);
        Assert.DoesNotContain("Math.Ceiling", method);
    }
    private static string MatchControllerSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "PicklinkBackend",
                "Services",
                "Matches",
                "MatchService.Open.cs");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate MatchService.Open.cs from the test output directory.");
    }

    private static string PaymentControllerSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "PicklinkBackend",
                "Services",
                "Payments",
                "PaymentService.cs");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate PaymentService.cs from the test output directory.");
    }

    private static string PaymentDtosSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "PicklinkBackend",
                "DTOs",
                "PaymentDtos.cs");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate PaymentDtos.cs from the test output directory.");
    }

    private static string MatchDtosSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "PicklinkBackend",
                "DTOs",
                "MatchRequest.cs");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate MatchRequest.cs from the test output directory.");
    }

    private static string SourcePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                [directory.FullName, "PicklinkBackend", .. segments]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)} from the test output directory.");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method signature: {signature}");

        var bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart >= 0, $"Could not find method body: {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] == '}') depth--;
            if (depth == 0) return source[start..(index + 1)];
        }

        return source[start..];
    }
}
