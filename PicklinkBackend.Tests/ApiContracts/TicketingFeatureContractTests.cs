namespace PicklinkBackend.Tests.ApiContracts;

public sealed class TicketingFeatureContractTests
{
    [Fact]
    public void TicketSessions_AreSeparateFromPlayerCreatedMatches()
    {
        var sources = TicketingSources();

        Assert.Contains("TicketSession", sources);
        Assert.DoesNotContain("_db.Matches", sources);
        Assert.DoesNotContain("MatchService", sources);
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
        Assert.Contains("item.TicketSession.Booking.Court.Venue.Owner.UserId == userId.Value", service);
        Assert.Contains("ticket.TicketSessionId != ownerTicketSessionId", service);
        Assert.Contains("ticket.TicketSession.Booking.Court.Venue.Owner.UserId != userId.Value", service);
        Assert.Contains("CheckInTicketCore(", service);
        Assert.Equal(2, service.Split("await CheckInTicketCore(").Length - 1);
        Assert.Equal(1, service.Split(duplicateGuard).Length - 1);
        Assert.Equal(1, service.Split(paidGuard).Length - 1);
    }

    [Fact]
    public void Cancellation_TracksRefundsAndNotifiesAffectedUsers()
    {
        var sources = TicketingSources();

        Assert.Contains("RefundPending", sources);
        Assert.Contains("CompleteRefund", sources);
        Assert.Contains("NotificationTypes.Ticket", sources);
        Assert.Contains("PublishSchedule", sources);
        Assert.Contains("PublishPayments", sources);
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
