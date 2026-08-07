namespace PicklinkBackend.Tests.ApiContracts;

public class MatchmakingP0ContractTests
{
    [Fact]
    public void PublicQueuesRedactPrivateLocationChatAndOtherJoinRequests()
    {
        var service = ReadRepositoryFile("PicklinkBackend", "Services", "Matches", "MatchmakingService.cs");

        Assert.Contains("SearchLatitude = null", service);
        Assert.Contains("SearchLongitude = null", service);
        Assert.Contains("ConversationId = null", service);
    }

    [Fact]
    public void QueueCommandsRequireMembershipOrHostAndSerializeCapacityChanges()
    {
        var service = ReadRepositoryFile("PicklinkBackend", "Services", "Matches", "MatchmakingService.cs");

        Assert.Contains("_matchRepository", service);
    }

    [Fact]
    public void WorkerLocksPlayersAndConsumesEveryActiveQueueBeforeMatching()
    {
        var worker = ReadRepositoryFile("PicklinkBackend", "Services", "Matches", "MatchmakingWorker.cs");

        Assert.Contains("var matchedPlayerIds = new HashSet<int>();", worker);
        Assert.Contains("matchmaking-player:{playerId}", worker);
        Assert.Contains("candidateQueueIds.OrderBy(id => id)", worker);
        Assert.Contains("activeSelectedCount != selectedQueueIds.Count", worker);
        Assert.Contains("candidateQueueIds.Contains(item.MatchmakingQueueId)", worker);
    }

    [Fact]
    public void InternalRealtimeWebhooksRequireTheSharedSecret()
    {
        var controller = ReadRepositoryFile("PicklinkBackend", "Controllers", "Matches", "MatchmakingController.cs");
        var worker = ReadRepositoryFile("PicklinkBackend", "Services", "Matches", "MatchmakingWorker.cs");

        Assert.Contains("CryptographicOperations.FixedTimeEquals", controller);
        Assert.Equal(2, controller.Split("if (!IsInternalRequest()) return Unauthorized();", StringSplitOptions.None).Length - 1);
        Assert.Contains("_matchRealtime.Publish", worker);
        Assert.Contains("_notificationRealtime.Publish", worker);
    }

    private static string ReadRepositoryFile(params string[] relativeSegments)
    {
        var fileName = relativeSegments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var rootDir = Path.Combine(directory.FullName, relativeSegments[0]);
            if (Directory.Exists(rootDir))
            {
                var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
                if (File.Exists(candidate)) return File.ReadAllText(candidate);

                var found = Directory.GetFiles(rootDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) return File.ReadAllText(found);
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
