namespace PicklinkBackend.Tests;

public class QueueInviteApiContractTests
{
    [Fact]
    public void MatchmakingControllerExposesInviteFriendEndpoint()
    {
        var filePath = Path.Combine(SourceDirectory("Controllers", "Matches"), "MatchmakingController.cs");
        var content = File.ReadAllText(filePath);

        Assert.Contains("[HttpPost(\"queues/{queueId:int}/invite\")]", content);
        Assert.Contains("InviteFriendToQueue(", content);
    }

    [Fact]
    public void MatchmakingServiceDefinesInviteFriendLogicWithNotifications()
    {
        var filePath = Path.Combine(SourceDirectory("Services", "Matches", "Implementations"), "MatchmakingService.cs");
        var content = File.ReadAllText(filePath);

        Assert.Contains("InviteFriendToQueue(", content);
        Assert.Contains("NotificationTypes.Match", content);
        Assert.Contains("_notifications.Add(", content);
        Assert.Contains("_notifications.PublishPending()", content);
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
