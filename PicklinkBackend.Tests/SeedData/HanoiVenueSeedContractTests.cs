namespace PicklinkBackend.Tests;

public class HanoiVenueSeedContractTests
{
    [Fact]
    public void DemoVenuesAreSeededAsMapVisibleVenues()
    {
        var source = File.ReadAllText(SeedPath("seed_hanoi_venues.sql"));

        Assert.Contains("v.approvalStatus = N'Approved'", source);
        Assert.Contains("v.isOpen = 1", source);
        Assert.Contains("[VENUE_LISTING_PAYMENT]", source);
        Assert.Contains("N'Confirmed'", source);
        Assert.Contains("paidUntil", source);
    }

    private static string SeedPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "database", "seeds", fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate database/seeds/{fileName}.");
    }
}
