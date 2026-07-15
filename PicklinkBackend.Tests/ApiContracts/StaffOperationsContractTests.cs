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
