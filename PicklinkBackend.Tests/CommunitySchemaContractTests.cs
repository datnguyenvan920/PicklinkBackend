namespace PicklinkBackend.Tests;

public class CommunitySchemaContractTests
{
    [Fact]
    public void StartupRepairsExistingSocialGroupColumnsUsedByCommunityQueries()
    {
        var program = File.ReadAllText(SourcePath("Program.cs"));

        Assert.Contains("COL_LENGTH(N'SOCIAL_GROUP', N'rules')", program);
        Assert.Contains("ALTER TABLE [SOCIAL_GROUP] ADD [rules] nvarchar(max) NULL;", program);
        Assert.Contains("COL_LENGTH(N'SOCIAL_GROUP', N'activeLocation')", program);
        Assert.Contains("ALTER TABLE [SOCIAL_GROUP] ADD [activeLocation] nvarchar(255) NULL;", program);
        Assert.Contains("COL_LENGTH(N'SOCIAL_GROUP', N'overallRating')", program);
        Assert.Contains("ALTER TABLE [SOCIAL_GROUP] ADD [overallRating] float NOT NULL", program);
        Assert.Contains("COL_LENGTH(N'SOCIAL_GROUP', N'ratingCount')", program);
        Assert.Contains("ALTER TABLE [SOCIAL_GROUP] ADD [ratingCount] int NOT NULL", program);
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
