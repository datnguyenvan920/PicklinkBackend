namespace PicklinkBackend.Tests.ApiContracts;

public sealed class TicketingFeatureContractTests
{
    [Fact]
    public void PublicTicketSessionPaging_StaysInSqlForTheCommonPath()
    {
        var service = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Services", "Ticketing", "Implementations", "TicketingService.cs"));

        Assert.Contains("var sqlTotalCount = await query.CountAsync(cancellationToken);", service);
        Assert.Contains("session.MaxPlayers > session.Tickets.Count", service);
        Assert.Contains(".Skip((page - 1) * pageSize)", service);
        Assert.Contains(".Take(pageSize)", service);
    }

    [Fact]
    public void TicketSessions_AreSeparateFromPlayerCreatedMatches()
    {
        var sources = TicketingSources();

        Assert.Contains("TicketSession", sources);
        Assert.DoesNotContain("_db.Matches", sources);
        Assert.DoesNotContain("MatchService", sources);
    }

    [Fact]
    public void OwnerCreation_BuildsDraftFromSelectedCourtAndTime()
    {
        var source = TicketingSources();

        Assert.Contains("request.Date.ToDateTime(request.StartTime)", source);
        Assert.Contains("OwnerEntryType = \"TicketSession\"", source);
        Assert.Contains("Status = \"Draft\"", source);
        Assert.DoesNotContain("b.BookingId == request.BookingId", source);
    }

    [Fact]
    public void PurchaseAndCheckIn_EnforceCapacityPaymentAndSingleUse()
    {
        var sources = TicketingSources();

        Assert.Contains("ticket-session:", sources);
        Assert.Contains("TicketingPolicy.OccupiesCapacity", sources);
        Assert.Contains("ticket.Status != " + (char)34 + "Paid" + (char)34, sources);
        Assert.Contains("ticket.Status == " + (char)34 + "CheckedIn" + (char)34, sources);
        Assert.Contains("ticket.CheckedInAt.HasValue", sources);
    }

    [Fact]
    public void Purchase_ReturnsQrAndUsesFiveMinutePaymentHold()
    {
        var purchase = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Services", "Ticketing", "Implementations", "TicketingService.Purchase.cs"));
        var service = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Services", "Ticketing", "Implementations", "TicketingService.cs"));
        var settings = File.ReadAllText(SourcePath("PicklinkBackend", "appsettings.json"));

        Assert.Contains("GetValue(\"Ticketing:PaymentHoldMinutes\", 5)", purchase);
        Assert.Contains("\"PaymentHoldMinutes\": 5", settings);
        Assert.Contains("QrImageUrl = ticket.Payment.QrImageUrl", service);
        Assert.Contains("TransferContent = ticket.Payment.TransferContent", service);
    }

    [Fact]
    public void TicketReceipt_CanBeSubmittedAndReviewedByTheVenueOwner()
    {
        var controller = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Controllers", "Payments", "PaymentController.cs"));
        var payments = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("tickets/{sessionTicketId:int}/submit", controller);
        Assert.Contains("SubmitTicketTransfer", payments);
        Assert.Contains("TicketReceiptSubmitted", payments);
        Assert.Contains("ticket.Status = \"Paid\"", payments);
        Assert.Contains("ticket.HoldExpiresAt = now.AddMinutes(holdMinutes)", payments);
        Assert.Contains("/owner/ticket-sessions/", payments);
    }

    [Fact]
    public void SessionLists_LoadPaymentsBeforeCountingReceiptsAwaitingReview()
    {
        var service = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Services", "Ticketing", "Implementations", "TicketingService.cs"));

        Assert.Equal(2, service.Split(
            ".Include(session => session.Tickets).ThenInclude(ticket => ticket.Payment)").Length - 1);
    }

    [Fact]
    public void OwnerCheckIn_RequiresOwnedSessionAndReusesPaymentAndSingleUseRules()
    {
        var controller = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Controllers", "Ticketing", "OwnerTicketSessionsController.cs"));
        var service = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Services", "Ticketing", "TicketingService.Staff.cs"));
        var quote = ((char)34).ToString();
        var duplicateGuard = "ticket.Status == " + quote + "CheckedIn" + quote
            + " || ticket.CheckedInAt.HasValue";
        var paidGuard = "ticket.Status != " + quote + "Paid" + quote
            + " || ticket.Payment.Status != " + quote + "Paid" + quote;

        Assert.Contains("[HttpPost(" + quote
            + "{ticketSessionId:int}/tickets/check-in" + quote + ")]", controller);
        Assert.Contains("_ticketing.CheckInOwnerTicket(", controller);
        Assert.Contains("[HttpPost(" + quote
            + "~/api/owner/tickets/check-in" + quote + ")]", controller);
        Assert.Contains("_ticketing.CheckInOwnerTicketByCode(", controller);
        Assert.Contains("item.TicketSession.Booking.Court.Venue.Owner.UserId == userId.Value", service);
        Assert.Contains("ticket.TicketSessionId != ownerTicketSessionId", service);
        Assert.Contains("ticket.TicketSession.Booking.Court.Venue.Owner.UserId != userId.Value", service);
        Assert.Contains("CheckInTicketCore(", service);
        // Owner scoped check-in, owner code-only check-in, and staff code-only check-in all
        // route through the same core so the capacity/paid/single-use guards stay in one place.
        Assert.Equal(3, service.Split("await CheckInTicketCore(").Length - 1);
        Assert.Equal(1, service.Split(duplicateGuard).Length - 1);
        Assert.Equal(1, service.Split(paidGuard).Length - 1);
    }

    [Fact]
    public void Cancellation_IsNonRefundableAndReleasesTheCourtBooking()
    {
        var sources = TicketingSources();
        var quote = ((char)34).ToString();

        Assert.Contains("ticket.Status = " + quote + "Cancelled" + quote, sources);
        Assert.Contains("session.Booking.Status = " + quote + "Cancelled" + quote, sources);
        Assert.Contains("paymentFrom is " + quote + "Pending" + quote
            + " or " + quote + "WaitingForConfirmation" + quote, sources);
        Assert.Contains("Vé đã thanh toán không được hoàn tiền", sources);
        Assert.Contains("var releaseStatus = isPaid ? " + quote + "Cancelled" + quote
            + " : " + quote + "Expired" + quote, sources);
        Assert.Contains("ticket.Payment.PaidAt is null", sources);
        Assert.DoesNotContain("CompleteRefund", sources);
        Assert.Contains("NotificationTypes.Ticket", sources);
        Assert.Contains("PublishSchedule", sources);
        Assert.Contains("PublishPayments", sources);
    }

    [Fact]
    public void OwnerCanIssueAnExplicitRefundAsAnOptInException()
    {
        // Self-service cancellation stays non-refundable by default (the test above), but the owner
        // can still choose to refund a specific paid ticket — e.g. when they cancel the whole
        // session and want to make players whole. This is opt-in, not automatic.
        var sources = TicketingSources();
        var controller = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Controllers", "Ticketing", "OwnerTicketSessionsController.cs"));
        var quote = ((char)34).ToString();

        Assert.Contains("[HttpPost(" + quote
            + "{ticketSessionId:int}/tickets/{sessionTicketId:int}/refund" + quote + ")]", controller);
        Assert.Contains("_ticketing.RefundOwnerTicket(", controller);
        Assert.Contains("RefundOwnerTicket(", sources);
        Assert.Contains("ticket.Status == " + quote + "CheckedIn" + quote
            + " || ticket.CheckedInAt.HasValue", sources);
        Assert.Contains("ticket.Payment.Status != " + quote + "Paid" + quote, sources);
        Assert.Contains("ticket.Payment.Status = " + quote + "RefundPending" + quote, sources);
    }

    [Fact]
    public void OwnerSessionLifecycle_EnforcesDateLocksStateAndCompleteMappings()
    {
        var source = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Services", "Ticketing", "Implementations", "TicketingService.cs"));

        Assert.Contains("private const int MaximumAdvanceBookingMonths = 1", source);
        Assert.Contains("ValidateAdvanceBookingDate(request.Date)", source);
        Assert.Contains("CourtScheduleResource(court.CourtId, startTime)", source);
        Assert.Contains("targetCourtId, targetStart, targetEnd, session.BookingId", source);
        Assert.Contains("session.PublishedAt = utcNow", source);
        Assert.Contains("session.Status != " + (char)34 + "Draft" + (char)34, source);
        Assert.Contains("CourtId = session.Booking.CourtId", source);
        Assert.Contains("CancellationDeadlineHours = session.CancellationDeadlineHours", source);
        Assert.Contains("PlayerEmail = ticket.Player.User.Email", source);
        Assert.Contains("CancelledAt = ticket.CancelledAt", source);
        Assert.Contains("CheckedInByStaffId = ticket.CheckedInByStaffId", source);
    }

    [Fact]
    public void Controllers_KeepRoleSpecificRoutesAndDelegateToService()
    {
        var sources = ControllerSources();
        var quote = ((char)34).ToString();

        Assert.Contains("[Authorize(Roles = " + quote + "VenueOwner" + quote + ")]", sources);
        Assert.Contains("[Authorize(Roles = " + quote + "Player" + quote + ")]", sources);
        Assert.Contains("[Authorize(Roles = " + quote + "Staff" + quote + ")]", sources);
        Assert.Contains("/api/staff/tickets/check-in", sources);
        Assert.DoesNotContain("_db.", sources);
        Assert.Contains("Contains(" + quote + ",CheckIn," + quote + ")", TicketingSources());
        Assert.DoesNotContain("Permissions.Contains(" + quote + "CheckIn" + quote + ")", TicketingSources());
        Assert.Contains("StaffTicketParticipantResponse", sources);
        Assert.DoesNotContain(
            "ActionResult<SessionTicketResponse>",
            File.ReadAllText(SourcePath(
                "PicklinkBackend", "Controllers", "Ticketing", "StaffTicketSessionsController.cs")));
    }

    [Fact]
    public void Migration_IsScopedToTicketingTables()
    {
        var migration = SourcePath("PicklinkBackend", "Migrations", "20260718055322_AddTicketSessions.cs");
        var source = File.ReadAllText(migration);

        Assert.Contains("TICKET_SESSION", source);
        Assert.Contains("SESSION_TICKET", source);
        Assert.DoesNotContain("Rename", source);
        Assert.DoesNotContain("MATCHMAKING_QUEUE", source);
        Assert.DoesNotContain("EnsureTicketingSchema(app)", File.ReadAllText(
            SourcePath("PicklinkBackend", "Startup", "SchemaStartup.cs")));
        var ledgerMigration = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Migrations", "20260718064712_AddSePayTransactionLedger.cs"));
        Assert.Contains("SEPAY_TRANSACTION", ledgerMigration);
        Assert.Contains("UQ_SEPAY_TRANSACTION_externalId", ledgerMigration);
        Assert.DoesNotContain("MATCHMAKING_QUEUE", ledgerMigration);
    }

    [Fact]
    public void SePayWebhook_ActivatesTicketWithoutFinalizingItsBooking()
    {
        var source = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Services", "Payments", "SePayWebhookService.cs"));
        var quote = ((char)34).ToString();

        Assert.Contains("ticket.Status = " + quote + "Paid" + quote, source);
        Assert.Contains(".Where(item => !ticketsByPaymentId.ContainsKey(item.PaymentId))", source);
        Assert.Contains("NotificationTypes.Ticket", source);
        Assert.Contains("ExternalTransactionId == request.Id", source);
        Assert.Contains("AdditionalRefundPending", source);
        Assert.Contains("NewSePayTransaction", source);
    }

    [Fact]
    public void GenericStaffOperations_ExcludeTicketSessionBookings()
    {
        var staffService = File.ReadAllText(SourcePath(
            "PicklinkBackend", "Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("IBookingRepository", staffService);
    }

    private static string TicketingSources() =>
        string.Join(Environment.NewLine,
            Directory.GetFiles(
                    SourcePath("PicklinkBackend", "Services", "Ticketing"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText));

    private static string ControllerSources() =>
        string.Join(Environment.NewLine,
            Directory.GetFiles(
                    SourcePath("PicklinkBackend", "Controllers", "Ticketing"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText));

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
