namespace PicklinkBackend.Tests;

public class FriendshipApiContractTests
{
    [Fact]
    public void CommunityDirectControllerExposesFriendshipEndpoints()
    {
        var filePath = Path.Combine(SourceDirectory("Controllers", "Community"), "CommunityController.Direct.cs");
        var content = File.ReadAllText(filePath);

        Assert.Contains("[HttpGet(\"friends\")]", content);
        Assert.Contains("[HttpGet(\"players/search\")]", content);
        Assert.Contains("[HttpGet(\"friends/statuses\")]", content);
        Assert.Contains("[HttpGet(\"friends/requests\")]", content);
        Assert.Contains("[HttpPost(\"friends/request\")]", content);
        Assert.Contains("[HttpPost(\"friends/accept\")]", content);
        Assert.Contains("[HttpPost(\"friends/decline\")]", content);
        Assert.Contains("[HttpDelete(\"friends/{targetUserId:int}\")]", content);
    }

    [Fact]
    public void CommunityServiceDefinesFriendshipOperations()
    {
        var filePath = Path.Combine(SourceDirectory("Services", "Community"), "ICommunityService.cs");
        var content = File.ReadAllText(filePath);

        Assert.Contains("GetFriends(", content);
        Assert.Contains("SearchPlayers(", content);
        Assert.Contains("GetFriendshipStatuses(", content);
        Assert.Contains("GetFriendRequests(", content);
        Assert.Contains("SendFriendRequest(", content);
        Assert.Contains("AcceptFriendRequest(", content);
        Assert.Contains("DeclineFriendRequest(", content);
        Assert.Contains("RemoveFriend(", content);
    }

    [Fact]
    public void CommunityDtosContainFriendshipContracts()
    {
        var filePath = Path.Combine(SourceDirectory("DTOs"), "CommunityDtos.cs");
        var content = File.ReadAllText(filePath);

        Assert.Contains("FriendResponse", content);
        Assert.Contains("FriendRequestResponse", content);
        Assert.Contains("FriendshipActionResponse", content);
        Assert.Contains("FriendshipStatusesResponse", content);
        Assert.Contains("PlayerSearchResultResponse", content);
    }

    [Fact]
    public void FriendRequestNotificationIsPersistedBeforeItsRealtimeEvent()
    {
        var filePath = Path.Combine(SourceDirectory("Services", "Community", "Implementations"), "CommunityService.Direct.cs");
        var content = File.ReadAllText(filePath);
        var methodStart = content.IndexOf(" SendFriendRequest(", StringComparison.Ordinal);
        var methodEnd = content.IndexOf(" AcceptFriendRequest(", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = content[methodStart..methodEnd];
        var notification = method.LastIndexOf("Title: \"Lời mời kết bạn mới\"", StringComparison.Ordinal);
        var save = method.IndexOf("SaveChangesAsync(cancellationToken)", notification, StringComparison.Ordinal);
        var publish = method.IndexOf("_notifications.PublishPending()", save, StringComparison.Ordinal);

        Assert.True(notification >= 0 && save > notification && publish > save);
        Assert.Contains("LinkTo: \"/posts/friends?tab=requests\"", method);
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
