namespace PicklinkBackend.Tests;

public class CommunitySchemaContractTests
{
    [Fact]
    public void StartupRepairsExistingSocialGroupColumnsUsedByCommunityQueries()
    {
        var schemaStartup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

        Assert.Contains("COL_LENGTH(N'SOCIAL_GROUP', N'rules')", schemaStartup);
        Assert.Contains("ALTER TABLE [SOCIAL_GROUP] ADD [rules] nvarchar(max) NULL;", schemaStartup);
        Assert.Contains("COL_LENGTH(N'SOCIAL_GROUP', N'activeLocation')", schemaStartup);
        Assert.Contains("ALTER TABLE [SOCIAL_GROUP] ADD [activeLocation] nvarchar(255) NULL;", schemaStartup);
        Assert.Contains("COL_LENGTH(N'SOCIAL_GROUP', N'overallRating')", schemaStartup);
        Assert.Contains("ALTER TABLE [SOCIAL_GROUP] ADD [overallRating] float NOT NULL", schemaStartup);
        Assert.Contains("COL_LENGTH(N'SOCIAL_GROUP', N'ratingCount')", schemaStartup);
        Assert.Contains("ALTER TABLE [SOCIAL_GROUP] ADD [ratingCount] int NOT NULL", schemaStartup);
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
