namespace PicklinkBackend.Tests;

public class CheckConstraintTrustSchemaContractTests
{
    [Fact]
    public void StartupCreatesAndRetrustsDomainCheckConstraints()
    {
        var startup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

        Assert.DoesNotContain("WITH NOCHECK", startup);
        Assert.Contains(
            "ALTER TABLE [RATING_HISTORY] WITH CHECK ADD CONSTRAINT [CK_RATING_HISTORY_score]",
            startup);
        Assert.Contains(
            "ALTER TABLE [RATING_HISTORY] WITH CHECK CHECK CONSTRAINT [CK_RATING_HISTORY_score]",
            startup);
        Assert.Contains(
            "ALTER TABLE [MATCH] WITH CHECK ADD CONSTRAINT [CK_MATCH_requiredPlayerCount]",
            startup);
        Assert.Contains("CHECK ([requiredPlayerCount] BETWEEN 2 AND 8)", startup);
        Assert.Contains(
            "ALTER TABLE [MATCH] WITH CHECK CHECK CONSTRAINT [CK_MATCH_requiredPlayerCount]",
            startup);
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
