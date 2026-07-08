namespace PicklinkBackend.Tests.ApiContracts;

public class ControllerSeparationContractTests
{
    [Fact]
    public void ControllersDoNotUseApplicationDbContextDirectly()
    {
        var controllerRoot = SourceDirectory("Controllers");
        var sources = Directory.GetFiles(controllerRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .ToList();

        Assert.NotEmpty(sources);
        foreach (var (path, source) in sources)
        {
            Assert.DoesNotContain("ApplicationDbContext", source);
            Assert.DoesNotContain("_dbContext", source);
            Assert.DoesNotContain("private readonly ApplicationDbContext", source);
        }
    }

    [Fact]
    public void LargeControllerLogicLivesBehindServices()
    {
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        foreach (var service in new[]
        {
            "PaymentService",
            "OwnerVenueService",
            "PlayerBookingService",
            "MatchService",
            "CommunityService"
        })
        {
            Assert.Contains($"services.AddScoped<{service}>()", services);
            Assert.True(File.Exists(SourcePath("Services", $"{service}.cs")), $"{service}.cs should exist.");
        }
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

    private static string SourceDirectory(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
