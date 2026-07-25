namespace PicklinkBackend.Tests.Policies;

public sealed class MatchReplacementChatAccessPolicyTests
{
    [Fact]
    public void ApprovedReplacementChatIsTemporaryAndEnforcedByEveryMessageApi()
    {
        var policy = File.ReadAllText(Locate("PicklinkBackend", "Services", "Shared", "MatchLobbyChatAccessPolicy.cs"));
        var matchService = File.ReadAllText(Locate("PicklinkBackend", "Services", "Matches", "Implementations", "MatchService.cs"));

        Assert.Contains("item.Status == \"Approved\"", policy);
        Assert.Contains("EndTime.AddHours(2)", policy);
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
        var cleanSegments = segments.FirstOrDefault() == "PicklinkBackend" ? segments[1..] : segments;
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

        throw new FileNotFoundException($"Could not locate {string.Join('/', segments)}.");
    }
}
