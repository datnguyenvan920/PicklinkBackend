using System.Reflection;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PicklinkBackend.Tests;

public class StartupConfigurationContractTests
{
    [Fact]
    public void DevelopmentStartupSkipsSchemaRepairAndDoesNotForceHttpsRedirect()
    {
        var developmentSettings = File.ReadAllText(SourcePath("appsettings.Development.json"));
        var program = File.ReadAllText(SourcePath("Program.cs"));
        var pipeline = File.ReadAllText(SourcePath("Startup", "ApplicationPipeline.cs"));

        // Dev tro vao DB dung chung cua nhom, nen startup khong tu ALTER schema hay seed lai tai khoan.
        Assert.Contains("\"RunSchemaChecks\": false", developmentSettings);
        Assert.Contains("\"Enabled\": false", developmentSettings);
        Assert.Contains("app.RunSchemaChecks();", program);
        Assert.Contains("app.UsePicklinkPipeline();", program);
        Assert.Contains("HttpsRedirection:Enabled", pipeline);
        Assert.Contains("app.UseHttpsRedirection();", pipeline);
    }

    [Fact]
    public void FrontendCorsOriginsComeFromConfiguration()
    {
        var settings = File.ReadAllText(SourcePath("appsettings.json"));
        var registration = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("Cors", settings);
        Assert.Contains("AllowedOrigins", settings);
        Assert.Contains("Cors:AllowedOrigins", registration);
        Assert.Contains("allowedFrontendOrigins.Any", registration);
        Assert.DoesNotContain(
            "origin.Equals",
            registration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendCorsPolicyAllowsConfiguredAppsAndRejectsUnknownOrigins()
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = new string('x', 64),
            ["Jwt:Issuer"] = "PicklinkBackend",
            ["Jwt:Audience"] = "PicklinkClients",
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=PicklinkCorsTest;",
            ["Cors:AllowedOrigins:0"] = "https://play.example.com",
            ["Cors:AllowedOrigins:1"] = "https://owner.example.com/",
            ["Cors:AllowedOrigins:2"] = "https://admin.example.com"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        var registrationType = typeof(PicklinkBackend.Program).Assembly
            .GetType("PicklinkBackend.Startup.ServiceRegistration", throwOnError: true)!;
        var registrationMethod = registrationType.GetMethod(
            "AddPicklinkServices",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        registrationMethod.Invoke(null, [services, configuration]);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = options.GetPolicy("FrontendPolicy");

        Assert.NotNull(policy);
        Assert.True(policy.IsOriginAllowed("https://play.example.com"));
        Assert.True(policy.IsOriginAllowed("https://owner.example.com"));
        Assert.True(policy.IsOriginAllowed("https://admin.example.com"));
        Assert.True(policy.IsOriginAllowed("http://localhost:3002"));
        Assert.False(policy.IsOriginAllowed("https://unknown.example.com"));
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
