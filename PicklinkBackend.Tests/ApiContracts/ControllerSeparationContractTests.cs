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

    [Fact]
    public void CommunityDiscoveryServiceIsPlainService()
    {
        var source = File.ReadAllText(SourcePath("Services", "CommunityDiscoveryService.cs"));

        Assert.DoesNotContain("ControllerBase", source);
        Assert.DoesNotContain("ActionResult", source);
        Assert.DoesNotContain("[Http", source);
    }
    [Fact]
    public void CommunityDirectConversationServiceIsPlainService()
    {
        var source = File.ReadAllText(SourcePath("Services", "CommunityDirectConversationService.cs"));

        Assert.DoesNotContain("ControllerBase", source);
        Assert.DoesNotContain("ActionResult", source);
        Assert.DoesNotContain("[Http", source);
    }

    [Fact]
    public void DirectConversationEndpointsAreNotHostedByCommunityBaseService()
    {
        var source = File.ReadAllText(SourcePath("Services", "CommunityService.Direct.cs"));

        Assert.DoesNotContain("conversations/direct", source);
        Assert.DoesNotContain("GetDirectConversations", source);
        Assert.DoesNotContain("SendDirectMessage", source);
    }
    [Fact]
    public void CommunityBaseServicePartialsDoNotHostHttpEndpoints()
    {
        var serviceRoot = SourceDirectory("Services");
        var sources = Directory.GetFiles(serviceRoot, "CommunityService*.cs")
            .Select(path => (Path: Path.GetFileName(path), Source: File.ReadAllText(path)))
            .ToList();

        Assert.NotEmpty(sources);
        foreach (var (path, source) in sources)
        {
            Assert.DoesNotContain("ControllerBase", source);
            Assert.DoesNotContain("ActionResult", source);
            Assert.DoesNotContain("[Http", source);
            Assert.DoesNotContain("[FromQuery]", source);
            Assert.DoesNotContain("[FromBody]", source);
            Assert.DoesNotContain("Microsoft.AspNetCore.Mvc", source);
        }
    }
    [Fact]
    public void MatchServicePartialsDoNotHostHttpEndpoints()
    {
        var serviceRoot = SourceDirectory("Services");
        var sources = Directory.GetFiles(serviceRoot, "MatchService*.cs")
            .Select(path => (Path: Path.GetFileName(path), Source: File.ReadAllText(path)))
            .ToList();

        Assert.NotEmpty(sources);
        foreach (var (path, source) in sources)
        {
            Assert.DoesNotContain("ControllerBase", source);
            Assert.DoesNotContain("ActionResult", source);
            Assert.DoesNotContain("[Http", source);
            Assert.DoesNotContain("[FromQuery]", source);
            Assert.DoesNotContain("[FromBody]", source);
            Assert.DoesNotContain("Microsoft.AspNetCore.Mvc", source);
        }
    }
    [Fact]
    public void PaymentServiceDoesNotHostHttpEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Services", "PaymentService.cs"));

        Assert.DoesNotContain("ControllerBase", source);
        Assert.DoesNotContain("ActionResult", source);
        Assert.DoesNotContain("[Http", source);
        Assert.DoesNotContain("[FromForm]", source);
        Assert.DoesNotContain("Microsoft.AspNetCore.Mvc", source);
    }
    [Fact]
    public void PlayerBookingServiceDoesNotHostHttpEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Services", "PlayerBookingService.cs"));

        Assert.DoesNotContain("ControllerBase", source);
        Assert.DoesNotContain("ActionResult", source);
        Assert.DoesNotContain("[Http", source);
        Assert.DoesNotContain("[From", source);
        Assert.DoesNotContain("Microsoft.AspNetCore.Mvc", source);
    }
    [Fact]
    public void OwnerVenueServiceDoesNotHostHttpEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Services", "OwnerVenueService.cs"));

        Assert.DoesNotContain("ControllerBase", source);
        Assert.DoesNotContain("ActionResult", source);
        Assert.DoesNotContain("[Http", source);
        Assert.DoesNotContain("[From", source);
        Assert.DoesNotContain("Microsoft.AspNetCore.Mvc", source);
    }
    [Fact]
    public void HttpWorkflowServicesUseSharedServiceResult()
    {
        foreach (var service in new[]
        {
            "MatchService.cs",
            "OwnerVenueService.cs",
            "PaymentService.cs",
            "PlayerBookingService.cs"
        })
        {
            var source = File.ReadAllText(SourcePath("Services", service));
            Assert.DoesNotContain("public enum MatchServiceResultStatus", source);
            Assert.DoesNotContain("public enum OwnerVenueServiceResultStatus", source);
            Assert.DoesNotContain("public enum PaymentServiceResultStatus", source);
            Assert.DoesNotContain("public enum PlayerBookingServiceResultStatus", source);
            Assert.DoesNotContain("public sealed record MatchServiceResult", source);
            Assert.DoesNotContain("public sealed record OwnerVenueServiceResult", source);
            Assert.DoesNotContain("public sealed record PaymentServiceResult", source);
            Assert.DoesNotContain("public sealed record PlayerBookingServiceResult", source);
        }

        Assert.True(File.Exists(SourcePath("Services", "ServiceResult.cs")), "Shared ServiceResult.cs should exist.");
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
