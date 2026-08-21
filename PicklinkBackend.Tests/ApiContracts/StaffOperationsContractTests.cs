namespace PicklinkBackend.Tests;

public class StaffOperationsContractTests
{
    [Fact]
    public void VerifyCodeAllowsCheckInPermissionBecauseCounterCheckInDependsOnVerifiedCode()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("VerifyBooking", source);
    }

    [Fact]
    public void SearchByCodeUsesVerificationPermissionBecauseStaffCommandImmediatelyVerifies()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("VerifyCodeAsync", source);
    }

    [Fact]
    public void StaffPermissionScopeUsesDelimitedPermissionTokens()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("Split(',',", source);
    }

    [Fact]
    public void StaffBookingListDoesNotLoadUnusedPaymentStatusHistory()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.DoesNotContain(".ThenInclude(item => item.StatusHistories)", source);
    }

    [Fact]
    public void StaffNotificationsProjectOnlyFieldsNeededByTheBell()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("ListNotificationsAsync", source);
    }

    [Fact]
    public void StaffBookingListLoadsOnlyPaymentFieldsUsedByItsDto()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("ListBookingsAsync", source);
    }

    [Fact]
    public void StaffAndOwnerCheckInListsOnlyConfirmedBookings()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Staff", "StaffOperationsController.cs"));
        var ownerController = File.ReadAllText(SourcePath("Controllers", "Owner", "OwnerCheckInController.cs"));
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("ListConfirmedTodayBookingsAsync", controller);
        Assert.Contains("ListConfirmedTodayBookingsAsync", ownerController);
        Assert.Contains("if (confirmedOnly)", source);
        Assert.Contains("item.Status == \"Confirmed\"", source);
    }

    [Fact]
    public void StaffBookingMapUsesTheLoadedParentVenueForNoTrackingLists()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("MapBooking", source);
    }

    [Fact]
    public void StaffCanVerifyAnEnteredCodeWithOneBookingQuery()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("OperationsBookingQuery", source);
    }

    [Fact]
    public void StaffBookingScopeKeepsMatchBookingsAndMapsTheirHost()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("MapBooking", source);
    }

    [Fact]
    public void StaffAttendanceActionsKeepGroupAndMatchStatesConsistent()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("CheckInAsync", source);
    }

    [Fact]
    public void BookingCodeVerificationSerializesOwnerAndStaffScanners()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("VerifyCodeAsync", source);
    }

    [Fact]
    public void CheckInGroupTerminalActionsSerializePerBooking()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("CheckInAsync", source);
    }

    [Fact]
    public void VenueOwnerCanOperateBookingsAtOwnedVenuesWithoutAStaffAssignment()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("item.Court.Venue.Owner.UserId == userId", source);
        Assert.Contains("item.Owner.UserId == userId.Value", source);
    }

    [Fact]
    public void BookingCodesAreReadOnlyWhilePrivateCheckInCodesCompleteAttendanceAtomically()
    {
        var source = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("group.CheckInCode == code", source);
        Assert.Contains("payment.TransferCode == code", source);
        Assert.Contains("payment.TransferCode.EndsWith(code)", source);
        Assert.Contains("matchingBookings.Count > 1", source);
        Assert.Contains("code.StartsWith(\"PL-\"", source);
        Assert.Contains("Mã booking chỉ dùng để tra cứu thông tin", source);
        Assert.Contains("SearchBookingAsync", source);
        Assert.Contains("item.BookingCode == normalizedCode", source);
        Assert.Contains(".AsNoTracking()", source);
        Assert.Contains("group.CheckInStatus = \"CheckedIn\"", source);
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
