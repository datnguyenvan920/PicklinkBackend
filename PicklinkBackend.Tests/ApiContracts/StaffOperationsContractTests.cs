namespace PicklinkBackend.Tests;

public class StaffOperationsContractTests
{
    [Fact]
    public void VerifyCodeAllowsCheckInPermissionBecauseCounterCheckInDependsOnVerifiedCode()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));

        Assert.Contains("ScopedBookings(userId.Value, \"VerifyBooking\", \"CheckIn\")", source);
    }

    [Fact]
    public void SearchByCodeUsesVerificationPermissionBecauseStaffCommandImmediatelyVerifies()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));
        var method = MethodBlock(source, "SearchBookingAsync", "GetBookingAsync");

        Assert.Contains("var booking = await ScopedBookings(userId.Value, \"VerifyBooking\", \"CheckIn\")", method);
        Assert.DoesNotContain("ScopedBookings(userId.Value, \"ViewBookings\")", method);
    }

    [Fact]
    public void StaffPermissionScopeUsesDelimitedPermissionTokens()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));

        Assert.Contains("var permissionToken = $\",{permission},\";", source);
        Assert.DoesNotContain("staff.Permissions.Contains(permission)", source);
    }

    [Fact]
    public void StaffBookingListDoesNotLoadUnusedPaymentStatusHistory()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));

        Assert.DoesNotContain(".ThenInclude(item => item.StatusHistories)", source);
        Assert.DoesNotContain(".Include(item => item.Slots)", source);
    }

    [Fact]
    public void StaffNotificationsProjectOnlyFieldsNeededByTheBell()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));
        var method = MethodBlock(source, "ListNotificationsAsync", "private IQueryable<Booking> ScopedBookings");

        Assert.Contains(".Select(item => new", method);
        Assert.Contains("PaymentMethod = item.Payments", method);
        Assert.DoesNotContain("booking.Payments", method);
    }

    [Fact]
    public void StaffBookingListLoadsOnlyPaymentFieldsUsedByItsDto()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));
        var method = MethodBlock(source, "ListTodayBookingsAsync", "SearchBookingAsync");

        Assert.Contains("includePayments: false", method);
        Assert.Contains("var paymentRows = await _dbContext.Payments.AsNoTracking()", method);
        Assert.Contains("item.PaymentMethod", method);
        Assert.Contains("item.Status", method);
    }

    [Fact]
    public void StaffCanVerifyAnEnteredCodeWithOneBookingQuery()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));
        var method = MethodBlock(source, "VerifyBookingCodeByCodeAsync", "VerifyBookingCodeAsync");

        Assert.Contains("ScopedBookings(userId.Value, \"VerifyBooking\", \"CheckIn\")", method);
        Assert.Contains("item.CheckInGroups.Any", method);
        Assert.DoesNotContain("CheckInCode.ToUpper()", method);
        Assert.Contains("SaveChangesAsync", method);
    }

    [Fact]
    public void StaffBookingScopeKeepsMatchBookingsAndMapsTheirHost()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));

        Assert.DoesNotContain("item.PlayerId != null && item.Court.Venue.Staff", source);
        Assert.Contains("acceptedParticipants.FirstOrDefault(item => item.IsHost)", source);
    }

    [Fact]
    public void StaffAttendanceActionsKeepGroupAndMatchStatesConsistent()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));

        Assert.Contains("SyncBookingCheckInStatusFromGroups(booking", source);
        Assert.Contains("group.CheckInStatus is \"CheckedIn\" or \"NoShow\"", source);
        Assert.Contains("PublishBookingChanged(booking, \"CheckInGroupCheckedIn\")", source);
        Assert.Contains("attendanceStatus == \"Present\" && operation.CodeVerifiedAt is null", source);
        Assert.DoesNotContain(
            "if (operation.CodeVerifiedAt is null)\n            return StaffOperationResult<StaffBookingResponse>.Conflict(\"Nhan vien phai xac minh ma don truoc khi diem danh.\");",
            source);
    }

    [Fact]
    public void CheckInGroupTerminalActionsSerializePerBooking()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));
        var checkIn = MethodBlock(source, "CheckInGroupAsync", "MarkGroupNoShowAsync");
        var noShow = MethodBlock(source, "MarkGroupNoShowAsync", "ConfirmAtCourtPaymentAsync");

        foreach (var method in new[] { checkIn, noShow })
        {
            Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)", method);
            Assert.Contains("SqlServerBookingLock.AcquireAsync", method);
            Assert.Contains("staff-checkin:{bookingId}", method);
            var save = method.IndexOf("SaveChangesAsync", StringComparison.Ordinal);
            var commit = method.IndexOf("CommitAsync", StringComparison.Ordinal);
            var publish = method.IndexOf("PublishBookingChanged", StringComparison.Ordinal);
            Assert.True(save >= 0 && commit > save && publish > commit,
                "Expected SaveChangesAsync -> CommitAsync -> PublishBookingChanged.");
        }
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }

    private static string MethodBlock(string source, string methodName, string nextMethodName)
    {
        var start = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not locate {methodName}.");
        var end = source.IndexOf(nextMethodName, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not locate method after {methodName}.");
        return source[start..end];
    }
}
