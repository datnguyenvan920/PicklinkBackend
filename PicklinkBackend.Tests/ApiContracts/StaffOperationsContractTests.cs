namespace PicklinkBackend.Tests;

public class StaffOperationsContractTests
{
    [Fact]
    public void VerifyCodeAllowsCheckInPermissionBecauseCounterCheckInDependsOnVerifiedCode()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Staff", "StaffOperationsController.cs"));

        Assert.Contains("ScopedBookings(userId.Value, \"VerifyBooking\", \"CheckIn\")", source);
    }

    [Fact]
    public void StaffPermissionScopeUsesDelimitedPermissionTokens()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Staff", "StaffOperationsController.cs"));

        Assert.Contains("var permissionToken = $\",{permission},\";", source);
        Assert.DoesNotContain("staff.Permissions.Contains(permission)", source);
    }

    [Fact]
    public void StaffBookingListDoesNotLoadUnusedPaymentStatusHistory()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Staff", "StaffOperationsController.cs"));

        Assert.DoesNotContain(".ThenInclude(item => item.StatusHistories)", source);
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
}
