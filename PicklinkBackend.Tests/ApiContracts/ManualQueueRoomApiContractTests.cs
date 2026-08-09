namespace PicklinkBackend.Tests;

public sealed class ManualQueueRoomApiContractTests
{
    [Fact]
    public void PublicManualQueueCanBeMaterializedWhenAViewerOpensItsDetails()
    {
        var service = File.ReadAllText(SourcePath("MatchmakingService.cs"));

        Assert.Contains("if (!queue.IsPublic)", service);
        Assert.Contains("if (queue.MatchId is int existingMatchId)", service);
        Assert.DoesNotContain("Chỉ chủ phòng mới có thể mở phòng.", service);
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
    public void AutomaticWorkerNeverConsumesOrDeletesPublicManualQueues()
    {
        var worker = File.ReadAllText(ServicePath("MatchmakingWorker.cs"));

        Assert.Contains("Where(q => q.IsActive && !q.IsPublic)", worker);
        Assert.Contains("Where(queue => queue.IsActive && !queue.IsPublic", worker);
        Assert.Contains("Origin = \"Automatic\"", worker);
    }

    [Fact]
    public void MaterializedManualRoomsKeepTheirOriginAfterQueueCleanup()
    {
        var service = File.ReadAllText(SourcePath("MatchmakingService.cs"));

        Assert.Contains("Origin = \"Manual\"", service);
    }

    [Fact]
    public void PublicManualQueuesIdentifyTheCurrentPlayerWithoutAssumingUserAndPlayerIdsMatch()
    {
        var service = File.ReadAllText(SourcePath("MatchmakingService.cs"));
        var dto = File.ReadAllText(DtoPath("MatchmakingDto.cs"));

        Assert.Contains("IsCurrentPlayer = qp.PlayerId == currentPlayerId", service);
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
