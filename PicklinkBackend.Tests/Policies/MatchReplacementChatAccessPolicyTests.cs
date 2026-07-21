namespace PicklinkBackend.Tests.Policies;

public sealed class MatchReplacementChatAccessPolicyTests
{
    [Fact]
    public void ApprovedReplacementChatIsTemporaryAndEnforcedByEveryMessageApi()
    {
        var policy = File.ReadAllText(Locate("PicklinkBackend", "Services", "Shared", "MatchLobbyChatAccessPolicy.cs"));
        var matchService = File.ReadAllText(Locate("PicklinkBackend", "Services", "Matches", "MatchService.cs"));
        var communityService = File.ReadAllText(Locate("PicklinkBackend", "Services", "Community", "CommunityDirectConversationService.cs"));
        var openMatchService = File.ReadAllText(Locate("PicklinkBackend", "Services", "Matches", "MatchService.Open.cs"));

        Assert.Contains("item.Status == \"Approved\"", policy);
        Assert.Contains("EndTime.AddHours(2)", policy);
        Assert.Contains("slot.RespondedAt ?? slot.RequestedAt", policy);
        Assert.Contains("Booking.Status == \"Holding\"", policy);
        Assert.Contains("Booking.Status == \"Confirmed\"", policy);
        Assert.True(matchService.Split("MatchLobbyChatAccessPolicy.ResolveAsync", StringSplitOptions.None).Length - 1 >= 2);
        Assert.True(communityService.Split("MatchLobbyChatAccessPolicy.ResolveAsync", StringSplitOptions.None).Length - 1 >= 3);
        Assert.Contains("message.SentAt >= chatAccess.VisibleFromUtc.Value", communityService);
        Assert.Contains("result.ChatAccessRole", openMatchService);
        Assert.Contains("VietnamTime.ToUtc(activeReplacementExpiry.Value)", openMatchService);
    }

    [Fact]
    public void ApprovedRoomMembersCanManageReplacementMembershipBeforeTheSlotStarts()
    {
        var replacements = File.ReadAllText(Locate("PicklinkBackend", "Services", "Matches", "MatchService.Replacements.cs"));
        var responses = File.ReadAllText(Locate("PicklinkBackend", "Services", "Matches", "MatchService.ReplacementResponses.cs"));
        var controller = File.ReadAllText(Locate("PicklinkBackend", "Controllers", "Matches", "MatchController.Open.cs"));

        Assert.Contains("ApprovedParticipants(match).Any(item => item.PlayerId == reviewerPlayerId.Value)", replacements);
        Assert.Contains("ReleaseApprovedSlotReplacementAsync(match, absence, replacementRequest, \"Left\"", replacements);
        Assert.Contains("ReleaseApprovedSlotReplacementAsync(match, absence, replacementRequest, \"Removed\"", replacements);
        Assert.Contains("absence.Status = \"Open\"", replacements);
        Assert.Contains("BookingCheckInGroup.StartTime <= VietnamTime.Now", replacements);
        Assert.Contains("canReviewReplacements || request.Status == \"Approved\"", responses);
        Assert.Contains("replacement-requests/{replacementRequestId:int}", controller);
        Assert.Contains("RemoveSlotReplacement", controller);
    }

    private static string Locate(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)}.");
    }
}
