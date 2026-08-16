namespace PicklinkBackend.Tests;

public class CommunityBusinessRulesTests
{
    [Fact]
    public void PostsEnforceVisibilityAtTheSharedServiceBoundary()
    {
        var posts = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityService.Posts.cs"));
        var helpers = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityService.Helpers.cs"));
        var controller = File.ReadAllText(SourcePath("Controllers", "Community", "CommunityController.Posts.cs"));

        Assert.Contains("post.Visibility == FriendsVisibility", posts);
        Assert.Contains("post.Visibility == PublicGroup", posts);
        Assert.Contains(".Where(post => post.GroupId == null && !post.IsHidden)", posts);
        Assert.Contains("CanViewPostAsync(post", posts);
        Assert.Contains("private async Task<bool> CanViewPostAsync", helpers);
        Assert.Contains("friendship.Status == AcceptedStatus", helpers);
        Assert.Contains("[FromQuery] int page = 1", controller);
        Assert.Contains("Pagination.NormalizePage(page)", posts);
        Assert.DoesNotContain("post.Visibility != PendingStatus", posts);
    }

    [Fact]
    public void ClubsSearchLocationProtectRatingsAndEnforceRoleHierarchy()
    {
        var groups = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityService.Groups.cs"));
        var members = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityService.Members.cs"));
        var helpers = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityService.Helpers.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "CommunityDtos.cs"));

        Assert.Contains("group.ActiveLocation", groups);
        Assert.Contains("CanManageMember(currentMember, member)", members);
        Assert.Contains("CanChangeMemberRole(currentMember, member, newRole)", members);
        Assert.Contains("RemoveGroupConversationParticipantAsync", members);
        Assert.Contains("actorRank > GroupRoleRank(target.Role)", helpers);

        var updateGroupDto = dtos.Split("public sealed record UpdateCommunityGroupRequest", 2)[1]
            .Split("public sealed record CreateCommunityPostRequest", 2)[0];
        Assert.DoesNotContain("OverallRating", updateGroupDto);
        Assert.DoesNotContain("RatingCount", updateGroupDto);
    }

    [Fact]
    public void CommunityResponsesExposePlayerIdsForHoverProfiles()
    {
        var dtos = File.ReadAllText(SourcePath("DTOs", "CommunityDtos.cs"));
        var posts = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityService.Posts.cs"));
        var members = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityService.Members.cs"));
        var comments = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityService.Comments.cs"));

        Assert.Contains("int? AuthorPlayerId = null", dtos);
        Assert.Contains("int? PlayerId = null", dtos);
        Assert.Contains("post.Author.Players", posts);
        Assert.Contains("groupMember.User.Players", members);
        Assert.Contains("comment.User.Players", comments);
    }

    [Fact]
    public void CommentLikesAndLobbyAccessAreLoadedInBatches()
    {
        var comments = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityService.Comments.cs"));
        var conversations = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityDirectConversationService.cs"));

        Assert.Contains("GetCommentLikeSummariesAsync(postId", comments);
        Assert.DoesNotContain("foreach (var c in comments)", comments);
        Assert.Contains("approvedMatchIds", conversations);
        Assert.Contains("temporaryAccessByMatchId", conversations);

        var listMethod = conversations.Split("GetDirectConversationsAsync", 2)[1]
            .Split("CountUnreadSendersAsync", 2)[0];
        Assert.DoesNotContain("await ResolveChatAccessAsync", listMethod);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
