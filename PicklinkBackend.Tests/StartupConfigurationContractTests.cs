namespace PicklinkBackend.Tests;

public class StartupConfigurationContractTests
{
    [Fact]
    public void DevelopmentStartupRepairsSchemaAndDoesNotForceHttpsRedirect()
    {
        var developmentSettings = File.ReadAllText(SourcePath("appsettings.Development.json"));
        var program = File.ReadAllText(SourcePath("Program.cs"));

        Assert.Contains("\"RunSchemaChecks\": true", developmentSettings);
        Assert.Contains("\"Enabled\": false", developmentSettings);
        Assert.Contains("HttpsRedirection:Enabled", program);
        Assert.Contains("app.UseHttpsRedirection();", program);
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
