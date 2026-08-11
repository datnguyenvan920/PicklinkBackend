namespace PicklinkBackend.Tests;

public sealed class ManualQueueRoomApiContractTests
{
    [Fact]
    public void PublicManualQueueCreatesItsRoomInTheSameCreationFlow()
    {
        var service = File.ReadAllText(SourcePath("MatchmakingService.cs"));
        var synchronization = File.ReadAllText(SourcePath("MatchQueueSynchronizationService.cs"));

        Assert.Contains("if (queueItem.IsPublic)", service);
        Assert.Contains("CreateManualMatchForQueueAsync(queueItem", service);
        Assert.Contains("queue.MatchId = match.MatchId", synchronization);
        Assert.Contains("Origin = \"Manual\"", synchronization);
    }

    [Fact]
    public void OpenMatchesCanBeFilteredByManualInvitationSource()
    {
        var service = File.ReadAllText(SourcePath("MatchService.cs"));

        Assert.Contains("string.Equals(source, \"manual\"", service);
        Assert.Contains("match.Origin == \"Manual\"", service);
        Assert.Contains("string.Equals(source, \"community\"", service);
        Assert.Contains("match.Origin == \"Community\"", service);
        Assert.Contains("LoadPreferredVenueLookupAsync(matches", service);
        Assert.Contains("PreferredVenues = preferredVenues ?? []", service);
        Assert.Contains("AvailableDateFrom = match.AvailableDateFrom.GetValueOrDefault()", service);
    }

    [Fact]
    public void AutomaticWorkerFillsTheLinkedManualRoomAndPreservesItsTicket()
    {
        var worker = File.ReadAllText(ServicePath("MatchmakingWorker.cs"));

        Assert.Contains("Where(q => q.IsActive && (!q.IsPublic || q.MatchId.HasValue))", worker);
        Assert.Contains("primaryQueue = queues.FirstOrDefault(queue => queue.MatchId.HasValue)", worker);
        Assert.Contains("ticket.MatchId == targetMatch.MatchId", worker);
        Assert.Contains("ticket.IsActive = false", worker);
        Assert.Contains("Origin = \"Automatic\"", worker);
    }

    [Fact]
    public void MaterializedManualRoomsKeepTheirOriginAfterQueueCleanup()
    {
        var service = File.ReadAllText(SourcePath("MatchQueueSynchronizationService.cs"));

        Assert.Contains("Origin = \"Manual\"", service);
    }

    [Fact]
    public void LinkedQueueAndRoomSynchronizeRosterAndEditableConditionsBothWays()
    {
        var synchronization = File.ReadAllText(SourcePath("MatchQueueSynchronizationService.cs"));
        var matchmaking = File.ReadAllText(SourcePath("MatchmakingService.cs"));
        var matches = File.ReadAllText(SourcePath("MatchService.cs"));

        Assert.Contains("SyncQueuePlayerToMatchAsync", synchronization);
        Assert.Contains("SyncMatchParticipantToQueueAsync", synchronization);
        Assert.Contains("SyncMatchDetailsToQueueAsync", synchronization);
        Assert.Contains("SyncQueuePlayerToMatchAsync(targetQueue, request", matchmaking);
        Assert.Contains("SyncMatchParticipantToQueueAsync", matches);
        Assert.Contains("SyncMatchDetailsToQueueAsync(match", matches);
    }

    [Fact]
    public void StartupReconcilesLegacyLinkedQueueRostersFromTheirRooms()
    {
        var startup = File.ReadAllText(StartupPath("SchemaStartup.cs"));

        Assert.Contains("EnsureLinkedQueueRosterData(app)", startup);
        Assert.Contains("INSERT INTO [MATCHMAKING_QUEUE_PLAYER]", startup);
        Assert.Contains("INNER JOIN [MATCH_PARTICIPANT]", startup);
        Assert.Contains("[existing].[playerId] = [participant].[playerId]", startup);
    }

    [Fact]
    public void HostedRepairMaterializesRoomsForLegacyUnlinkedManualQueues()
    {
        var repair = File.ReadAllText(ServicePath("LegacyManualQueueRoomRepairService.cs"));
        var registration = File.ReadAllText(StartupPath("ServiceRegistration.cs"));

        Assert.Contains("queue.IsPublic", repair);
        Assert.Contains("!queue.MatchId.HasValue", repair);
        Assert.Contains("SqlServerBookingLock.AcquireAsync", repair);
        Assert.Contains("CreateManualMatchForQueueAsync(queue", repair);
        Assert.Contains("AddHostedService<LegacyManualQueueRoomRepairService>()", registration);
    }

    [Fact]
    public void PublicManualQueuesIdentifyTheCurrentPlayerWithoutAssumingUserAndPlayerIdsMatch()
    {
        var service = File.ReadAllText(SourcePath("MatchmakingService.cs"));
        var dto = File.ReadAllText(DtoPath("MatchmakingDto.cs"));

        Assert.Contains("IsCurrentPlayer = qp.PlayerId == currentPlayerId", service);
        Assert.Contains("IsCurrentPlayer = qp.PlayerId == player.PlayerId", service);
        Assert.Contains("public bool IsCurrentPlayer", dto);
    }

    [Fact]
    public void FullPublicManualQueuesAreNotDiscoverableByOpponents()
    {
        var service = File.ReadAllText(SourcePath("MatchmakingService.cs"));

        Assert.Contains("q.QueuePlayers.Count(qp => qp.Status == \"Approved\") < q.PlayerCount", service);
    }

    private static string SourcePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "PicklinkBackend",
                "Services",
                "Matches",
                "Implementations",
                fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName}.");
    }

    private static string StartupPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "PicklinkBackend", "Startup", fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName}.");
    }

    private static string ServicePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "PicklinkBackend", "Services", "Matches", fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName}.");
    }

    private static string DtoPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "PicklinkBackend", "DTOs", fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName}.");
    }
}
