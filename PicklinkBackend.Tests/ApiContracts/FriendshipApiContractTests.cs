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
