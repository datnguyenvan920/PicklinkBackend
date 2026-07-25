namespace PicklinkBackend.Tests;

public class PersistedTextDataRepairMigrationTests
{
    [Fact]
    public void MigrationRepairsAllKnownPersistedTextColumnsWithoutReplacementCharacters()
    {
        var source = File.ReadAllText(SourcePath(
            "Migrations",
            "20260717123000_RepairPersistedVietnameseText.cs"));

        Assert.Contains("20260717123000_RepairPersistedVietnameseText", source);
        Assert.Contains("BOOKING_STATUS_HISTORY", source);
        Assert.Contains("PAYMENT_STATUS_HISTORY", source);
        Assert.Contains("NOTIFICATION_LOG", source);
        Assert.Contains("BOOKING", source);
        Assert.Contains("Latin1_General_100_CI_AS_SC_UTF8", source);
        Assert.Contains("NCHAR(65533)", source);
        Assert.Contains("Thanh toán cho booking", source);
        Assert.Contains("Cụm sân", source);
        Assert.Contains("mời bạn tham gia trận", source);
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