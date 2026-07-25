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
        var source = File.ReadAllText(SourcePath("Services", "Community", "CommunityDiscoveryService.cs"));

        Assert.DoesNotContain("ControllerBase", source);
        Assert.DoesNotContain("ActionResult", source);
        Assert.DoesNotContain("[Http", source);
    }
    [Fact]
    public void CommunityDirectConversationServiceIsPlainService()
    {
        var source = File.ReadAllText(SourcePath("Services", "Community", "CommunityDirectConversationService.cs"));

        Assert.DoesNotContain("ControllerBase", source);
        Assert.DoesNotContain("ActionResult", source);
        Assert.DoesNotContain("[Http", source);
    }

    [Fact]
    public void DirectConversationCreationIsAtomicAndPairLocked()
    {
        var source = File.ReadAllText(SourcePath("Services", "Community", "CommunityDirectConversationService.cs"));
        var method = source[
            source.IndexOf("StartDirectConversationAsync", StringComparison.Ordinal)..source.IndexOf("GetDirectConversationsAsync", StringComparison.Ordinal)];

        Assert.Contains("Math.Min(userId.Value, targetUserId)", method);
        Assert.Contains("Math.Max(userId.Value, targetUserId)", method);
        Assert.Contains("BeginTransactionAsync", method);
        Assert.Contains("SqlServerBookingLock.AcquireAsync", method);
        Assert.Contains("direct-conversation:{firstUserId}:{secondUserId}", method);
        Assert.Contains("transaction.CommitAsync", method);
        Assert.Equal(1, method.Split("SaveChangesAsync", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, method.Split("conversation.ConversationParticipants.Add", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void DirectConversationInboxUsesOneSetBasedQuery()
    {
        var source = File.ReadAllText(SourcePath("Services", "Community", "CommunityDirectConversationService.cs"));
        var method = source[
            source.IndexOf("GetDirectConversationsAsync", StringComparison.Ordinal)..source.IndexOf("GetDirectMessagesAsync", StringComparison.Ordinal)];

        Assert.Contains(".Select(c => new", method);
        Assert.Contains("LastMessage = c.Messages", method);
        Assert.Contains("OtherParticipant = c.ConversationParticipants", method);
        Assert.Equal(1, method.Split("ToListAsync", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("FirstOrDefaultAsync", method);
    }

    [Fact]
    public void UnreadMessageBadgeCountsDistinctSendersAndUsesTheReadCursor()
    {
        var service = File.ReadAllText(SourcePath("Services", "Community", "CommunityDirectConversationService.cs"));
        var controller = File.ReadAllText(SourcePath("Controllers", "Community", "CommunityController.Direct.cs"));
        var groupMessages = File.ReadAllText(SourcePath("Services", "Community", "CommunityService.GroupMessages.cs"));

        Assert.Contains("CountUnreadSendersAsync", service);
        Assert.Contains("participant.LastReadAt", service);
        Assert.Contains("participant.JoinedAt", service);
        Assert.Contains(".Select(message => message.SenderId)", service);
        Assert.Contains(".Distinct()", service);
        Assert.Contains("conversations/unread-sender-count", controller);
        Assert.Contains("participant.LastReadAt = DateTime.UtcNow", groupMessages);
        Assert.Contains("UnreadMessageCount = c.ConversationParticipants", service);
        Assert.Contains("UnreadMessageCount", File.ReadAllText(SourcePath("DTOs", "CommunityDtos.cs")));
        var groupList = File.ReadAllText(SourcePath("Services", "Community", "CommunityService.Groups.cs"));
        Assert.Contains("participant.LastReadAt", groupList);
        Assert.Contains("participant.Conversation.GroupId == group.GroupId", groupList);
    }

    [Fact]
    public void DirectConversationEndpointsAreNotHostedByCommunityBaseService()
    {
        var source = File.ReadAllText(SourcePath("Services", "Community", "CommunityService.Direct.cs"));

        Assert.DoesNotContain("conversations/direct", source);
        Assert.DoesNotContain("GetDirectConversations", source);
        Assert.DoesNotContain("SendDirectMessage", source);
    }
    [Fact]
    public void CommunityBaseServicePartialsDoNotHostHttpEndpoints()
    {
        var serviceRoot = SourceDirectory("Services");
        var sources = Directory.GetFiles(serviceRoot, "CommunityService*.cs", SearchOption.AllDirectories)
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
        var sources = Directory.GetFiles(serviceRoot, "MatchService*.cs", SearchOption.AllDirectories)
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
        var source = File.ReadAllText(SourcePath("Services", "Payments", "PaymentService.cs"));

        Assert.DoesNotContain("ControllerBase", source);
        Assert.DoesNotContain("ActionResult", source);
        Assert.DoesNotContain("[Http", source);
        Assert.DoesNotContain("[FromForm]", source);
        Assert.DoesNotContain("Microsoft.AspNetCore.Mvc", source);
    }
    [Fact]
    public void PlayerBookingServiceDoesNotHostHttpEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));

        Assert.DoesNotContain("ControllerBase", source);
        Assert.DoesNotContain("ActionResult", source);
        Assert.DoesNotContain("[Http", source);
        Assert.DoesNotContain("[From", source);
        Assert.DoesNotContain("Microsoft.AspNetCore.Mvc", source);
    }
    [Fact]
    public void OwnerVenueServiceDoesNotHostHttpEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Services", "Owner", "OwnerVenueService.cs"));

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

        Assert.True(File.Exists(SourcePath("Services", "Shared", "ServiceResult.cs")), "Shared ServiceResult.cs should exist.");
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
