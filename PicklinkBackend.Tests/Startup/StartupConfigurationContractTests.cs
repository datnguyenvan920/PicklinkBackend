namespace PicklinkBackend.Tests;

public class StartupConfigurationContractTests
{
    [Fact]
    public void DevelopmentStartupRepairsSchemaAndDoesNotForceHttpsRedirect()
    {
        var developmentSettings = File.ReadAllText(SourcePath("appsettings.Development.json"));
        var program = File.ReadAllText(SourcePath("Program.cs"));
        var pipeline = File.ReadAllText(SourcePath("Startup", "ApplicationPipeline.cs"));

        Assert.Contains("\"RunSchemaChecks\": true", developmentSettings);
        Assert.Contains("\"Enabled\": false", developmentSettings);
        Assert.Contains("app.RunSchemaChecks();", program);
        Assert.Contains("app.UsePicklinkPipeline();", program);
        Assert.Contains("HttpsRedirection:Enabled", pipeline);
        Assert.Contains("app.UseHttpsRedirection();", pipeline);
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
