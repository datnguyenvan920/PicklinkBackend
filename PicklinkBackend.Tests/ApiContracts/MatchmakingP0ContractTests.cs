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
        Assert.Contains(".Where(qp => IsApproved(qp) || qp.PlayerId == currentPlayerId)", service);
    }

    [Fact]
    public void QueueCommandsRequireMembershipOrHostAndSerializeCapacityChanges()
    {
        var service = ReadRepositoryFile("PicklinkBackend", "Services", "Matches", "MatchmakingService.cs");

        Assert.Contains("queueItem.QueuePlayers.Any(qp => qp.PlayerId == player.PlayerId && IsApproved(qp))", service);
        Assert.Contains("item.IsHost && item.Player.UserId == userId && item.Status == \"Approved\"", service);
        Assert.True(
            service.Split("matchmaking-queue:{queueId}", StringSplitOptions.None).Length - 1 >= 3,
            "Join, room creation and request review must serialize on the queue.");
        Assert.Contains("BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)", service);
    }

    [Fact]
    public void WorkerLocksPlayersAndConsumesEveryActiveQueueBeforeMatching()
    {
        var worker = ReadRepositoryFile("PicklinkBackend.MatchmakingWorker", "MatchmakingWorker.cs");

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
        var worker = ReadRepositoryFile("PicklinkBackend.MatchmakingWorker", "MatchmakingWorker.cs");

        Assert.Contains("CryptographicOperations.FixedTimeEquals", controller);
        Assert.Equal(2, controller.Split("if (!IsInternalRequest()) return Unauthorized();", StringSplitOptions.None).Length - 1);
        Assert.Contains("X-Picklink-Worker-Secret", worker);
        Assert.Contains("MatchmakingWorker:InternalSecret", worker);
    }

    private static string ReadRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
