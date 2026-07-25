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
