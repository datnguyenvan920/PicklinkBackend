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

    [Fact]
    public void StartupCreatesGroupImageTableUsedByClubDetail()
    {
        var schemaStartup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

        Assert.Contains("OBJECT_ID(N'[GROUP_IMAGE]', N'U') IS NULL", schemaStartup);
        Assert.Contains("CREATE TABLE [GROUP_IMAGE]", schemaStartup);
        Assert.Contains("[groupImageId] int IDENTITY(1,1)", schemaStartup);
        Assert.Contains("[groupId] int NOT NULL", schemaStartup);
        Assert.Contains("CONSTRAINT [FK_GROUP_IMAGE_GROUP]", schemaStartup);
        Assert.Contains("CREATE INDEX [IX_GROUP_IMAGE_groupId]", schemaStartup);
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
