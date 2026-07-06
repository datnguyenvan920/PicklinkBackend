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
