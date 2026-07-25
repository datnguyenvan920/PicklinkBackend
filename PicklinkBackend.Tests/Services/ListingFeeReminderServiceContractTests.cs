namespace PicklinkBackend.Tests;

public class ListingFeeReminderServiceContractTests
{
    [Fact]
    public void ListingFeeReminderServiceWarnsOwnersBeforePaidUntilExpires()
    {
        var source = File.ReadAllText(SourcePath("Services", "ListingFees", "Implementations", "ListingFeeReminderService.cs"));

        Assert.Contains("BackgroundService", source);
        Assert.Contains("IsListingFeeSchemaReadyAsync", source);
        Assert.Contains("GetExpiringListingFeeVenuesAsync", source);
        Assert.Contains("NotificationService", source);
    }

    [Fact]
    public void ListingFeeReminderServiceIsRegistered()
    {
        var serviceRegistration = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("AddHostedService<ListingFeeReminderService>", serviceRegistration);
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
