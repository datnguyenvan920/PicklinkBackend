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
        Assert.Contains(
            "ALTER TABLE [MATCH] WITH CHECK CHECK CONSTRAINT [CK_MATCH_requiredPlayerCount]",
            startup);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidates = new[]
            {
                Path.Combine(new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray()),
                Path.Combine(new[] { directory.FullName, "PicklinkBackend", "PicklinkBackend" }.Concat(relativeSegments).ToArray())
            };
            var candidate = candidates.FirstOrDefault(File.Exists);
            if (candidate is not null) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
