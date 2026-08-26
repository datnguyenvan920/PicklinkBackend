namespace PicklinkBackend.Tests;

public class MatchApiContractTests
{
    [Fact]
    public void MatchControllerSupportsFrontendPluralMatchesRoute()
    {
        var root = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchController.cs"));
        var open = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchController.Open.cs"));
        var recommendations = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchController.Recommendations.cs"));

        Assert.Contains("[Route(\"api/matches\")]", root);
        Assert.DoesNotContain("[Route(\"api/[controller]\")]", root);
        Assert.Contains("[HttpGet(\"venues\")]", open);
        Assert.Contains("[HttpGet(\"open\")]", open);
        Assert.Contains("[HttpPost(\"open\")]", open);
        Assert.Contains("[HttpGet(\"player-recommendations\")]", recommendations);
        Assert.Contains("[HttpPost(\"{matchId:int}/invitations\")]", recommendations);
    }

    [Fact]
    public void RecruitingMorePlayersReopensTheRoomAndItsLinkedQueue()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Recommendations.cs"));
        var queueSyncSource = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchQueueSynchronizationService.cs"));
        var methodStart = source.IndexOf(" InviteMatchPlayers(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf(" AcceptMatchInvitation(", methodStart, StringComparison.Ordinal);
        var queueSyncStart = queueSyncSource.IndexOf(" SyncMatchDetailsToQueueAsync(", StringComparison.Ordinal);
        var queueSyncEnd = queueSyncSource.IndexOf(" SyncQueueToFirebaseAsync(", queueSyncStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        Assert.True(queueSyncStart >= 0 && queueSyncEnd > queueSyncStart);
        var method = source[methodStart..methodEnd];
        var queueSyncMethod = queueSyncSource[queueSyncStart..queueSyncEnd];

        Assert.Contains("MatchRoomLifecyclePolicy.CanReopenRecruitment", method);
        Assert.Contains("match.Status = MatchRoomLifecyclePolicy.Recruiting", method);
        Assert.Contains("SyncMatchDetailsToQueueAsync(match", method);
        Assert.Contains("SyncQueueToFirebaseAsync(linkedQueue", method);
        Assert.Contains("match.MatchParticipants.Count", queueSyncMethod);
        Assert.Contains("MatchRoomLifecyclePolicy.IsRoomMemberStatus", queueSyncMethod);
        Assert.DoesNotContain("CountApproved(queue.QueuePlayers)", queueSyncMethod);
        Assert.DoesNotContain("if (match.Status != \"Recruiting\")", method);
        Assert.True(
            method.IndexOf("match.Status = MatchRoomLifecyclePolicy.Recruiting", StringComparison.Ordinal)
            < method.IndexOf("SyncMatchDetailsToQueueAsync(match", StringComparison.Ordinal));
    }

    [Fact]
    public void OpenMatchDiscoveryKeepsManualRoomsWithoutAnActivePublicQueueTicket()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var dto = File.ReadAllText(SourcePath("DTOs", "MatchRequest.cs"));
        var methodStart = source.IndexOf(" GetOpenMatches(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf(" GetMyOpenMatches(", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("!_matchRepository.MatchmakingQueues.Any(queue =>", method);
        Assert.Contains("queue.MatchId == match.MatchId", method);
        Assert.Contains("queue.IsActive", method);
        Assert.Contains("queue.IsPublic", method);
        Assert.DoesNotContain("query = query.Where(match => match.Origin != \"Manual\" ||", method);
        Assert.Contains("public string Origin", dto);
        Assert.Contains("public string ReplayType", dto);
        Assert.Contains("Origin = match.Origin", source);
        Assert.Contains("ReplayType = match.ReplayType", source);
    }

    [Fact]
    public void MatchCheckInUsesThePaidPlayersExistingUniqueTransferCode()
    {
        var open = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var staff = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));
        var dto = File.ReadAllText(SourcePath("DTOs", "StaffOperationsDtos.cs"));

        Assert.Contains("_matchRepository", open);
        Assert.Contains("OperationsBookingQuery", staff);
        Assert.Contains("public int? VerifiedPlayerId", dto);
    }

    [Fact]
    public void MarkReadyToBookPersistsTheReadyStateAndReturnsTheUpdatedRoom()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Open.cs"));
        var methodStart = source.IndexOf(" MarkReadyToBook(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf(" CreateMatchBooking(", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("match.HostPlayerId != player.PlayerId", method);
        Assert.Contains("approvedCount < match.RequiredPlayerCount", method);
        Assert.Contains("match.Status = \"ReadyToBook\"", method);
        Assert.Contains("SaveChangesAsync", method);
        Assert.Contains("CommitAsync", method);
        Assert.Contains("_matchRealtime.Publish(matchId, \"ReadyToBook\")", method);
        Assert.Contains("LoadOpenMatchResponseAsync(matchId, player.PlayerId", method);
        Assert.DoesNotContain("new OpenMatchDetailResponse()", method);
    }

    [Fact]
    public void JoinOpenMatchPersistsAPendingRequestAndNotifiesTheHost()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var methodStart = source.IndexOf(" JoinOpenMatch(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf(" LeaveOpenMatch(", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("Status = \"Pending\"", method);
        Assert.Contains("AddParticipantAsync", method);
        Assert.Contains("SaveChangesAsync", method);
        Assert.Contains("CommitAsync", method);
        Assert.Contains("UserId: host.UserId", method);
        Assert.Contains("_notifications.PublishPending()", method);
        Assert.Contains("_matchRealtime.Publish(matchId, \"JoinRequested\")", method);
        Assert.Contains("LoadOpenMatchResponseAsync(matchId, player.PlayerId", method);
        Assert.DoesNotContain("new OpenMatchDetailResponse()", method);
    }

    [Fact]
    public void RoomRosterLetsEveryoneLeaveAndOnlyLetsHostsRemoveMembersAfterTheBookingUnlocks()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var dto = File.ReadAllText(SourcePath("DTOs", "MatchRequest.cs"));
        var leaveStart = source.IndexOf(" LeaveOpenMatch(", StringComparison.Ordinal);
        var leaveEnd = source.IndexOf(" AcceptParticipant(", leaveStart, StringComparison.Ordinal);
        var removeStart = source.IndexOf(" RemoveParticipant(", StringComparison.Ordinal);
        var removeEnd = source.IndexOf(" GetMatchSlotOptions(", removeStart, StringComparison.Ordinal);

        Assert.True(leaveStart >= 0 && leaveEnd > leaveStart);
        Assert.True(removeStart >= 0 && removeEnd > removeStart);
        var leave = source[leaveStart..leaveEnd];
        var remove = source[removeStart..removeEnd];

        Assert.Contains("OrderBy(item => item.RequestedAt)", leave);
        Assert.Contains("match.HostPlayerId = nextHost.PlayerId", leave);
        Assert.Contains("match.Status = \"Cancelled\"", leave);
        Assert.Contains("SyncMatchParticipantToQueueAsync(", leave);
        Assert.Contains("HasRosterLockedBooking(match, VietnamTime.Now, DateTime.UtcNow)", remove);
        Assert.DoesNotContain("match.Status is not (\"Recruiting\" or \"ReadyToBook\")", remove);
        Assert.Contains("public bool CanRemoveParticipants", dto);
    }
    [Fact]
    public void MatchDetailOnlyReturnsPersonalCheckInCodeAfterPaymentInsideTheCheckInWindow()
    {
        var detailSource = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var visibilitySource = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.ReplacementResponses.cs"));

        Assert.Contains("BuildVisibleBookingRoundsAsync(", detailSource);
        Assert.DoesNotContain("CheckInCode = g.CheckInCode", detailSource);
        Assert.Contains("payment.PayerId == payingPlayerId && payment.Status == \"Paid\"", visibilitySource);
        Assert.Contains("booking.Status == \"Confirmed\"", visibilitySource);
        Assert.Contains("localNow >= group.StartTime.AddMinutes(-30)", visibilitySource);
        Assert.Contains("CheckInCode.Compact(playerPayment?.TransferCode)", visibilitySource);
    }

    [Fact]
    public void InvitationListsUseLeanSplitQueriesInsteadOfLoadingTheDetailGraph()
    {
        var matches = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var queues = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchmakingService.cs"));
        var methodStart = matches.IndexOf("private static IQueryable<Match> BaseMatchListQuery(", StringComparison.Ordinal);
        var methodEnd = matches.IndexOf("private Task<Match?> GetMatchGraphAsync(", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var listGraph = matches[methodStart..methodEnd];

        Assert.Contains("AsSplitQuery()", listGraph);
        Assert.Contains("match.AvailabilitySlots", listGraph);
        Assert.Contains("match.MatchParticipants", listGraph);
        Assert.Contains("match.Bookings", listGraph);
        Assert.DoesNotContain("booking.Payments", listGraph);
        Assert.DoesNotContain("match.Scorecards", listGraph);
        Assert.True(queues.Split(".AsSplitQuery()", StringSplitOptions.None).Length >= 3);
    }

    [Fact]
    public void MatchRoomDetailUsesANoTrackingSplitQueryGraph()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var queryStart = source.IndexOf("private IQueryable<Match> MatchInvitationQuery()", StringComparison.Ordinal);
        var queryEnd = source.IndexOf("private static IEnumerable<MatchParticipant> ApprovedParticipants", queryStart, StringComparison.Ordinal);
        var detailStart = source.IndexOf("private async Task<OpenMatchDetailResponse?> LoadOpenMatchResponseAsync", StringComparison.Ordinal);
        var detailEnd = source.IndexOf("private async Task AddConversationParticipantAsync", detailStart, StringComparison.Ordinal);

        Assert.True(queryStart >= 0 && queryEnd > queryStart);
        Assert.True(detailStart >= 0 && detailEnd > detailStart);
        var detailGraph = source[queryStart..queryEnd];
        var detailLoader = source[detailStart..detailEnd];

        Assert.Contains("AsSplitQuery()", detailGraph);
        Assert.Contains("m.AvailabilitySlots", detailGraph);
        Assert.Contains("m.MatchParticipants", detailGraph);
        Assert.Contains("m.Bookings", detailGraph);
        Assert.Contains("m.SlotAbsences", detailGraph);
        Assert.Contains("MatchDetailCoreQuery()", detailLoader);
        Assert.Contains("PopulateBookingRoundPageAsync(", detailLoader);
        Assert.Contains(".AsNoTracking()", detailLoader);
    }

    [Fact]
    public void MatchRoomDetailDisplaysTheLatestActiveBookingAfterEditing()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var detailStart = source.IndexOf("private async Task<OpenMatchDetailResponse?> LoadOpenMatchResponseAsync", StringComparison.Ordinal);
        var detailEnd = source.IndexOf("private async Task AddConversationParticipantAsync", detailStart, StringComparison.Ordinal);

        Assert.True(detailStart >= 0 && detailEnd > detailStart);
        var detailLoader = source[detailStart..detailEnd];

        Assert.Contains("VenueId = firstBooking?.Court.VenueId", detailLoader);
        Assert.Contains("CourtId = firstBooking?.CourtId", detailLoader);
        Assert.Contains("StartTime = firstBooking?.StartTime", detailLoader);
        Assert.Contains("EndTime = firstBooking?.EndTime", detailLoader);
    }

    [Fact]
    public void MatchRoomDetailReturnsTheExactSlotsOfTheCurrentBooking()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var dto = File.ReadAllText(SourcePath("DTOs", "MatchRequest.cs"));
        var queryStart = source.IndexOf("private IQueryable<Match> MatchInvitationQuery()", StringComparison.Ordinal);
        var queryEnd = source.IndexOf("private static IEnumerable<MatchParticipant> ApprovedParticipants", queryStart, StringComparison.Ordinal);
        var detailStart = source.IndexOf("private async Task<OpenMatchDetailResponse?> LoadOpenMatchResponseAsync", StringComparison.Ordinal);
        var detailEnd = source.IndexOf("private async Task AddConversationParticipantAsync", detailStart, StringComparison.Ordinal);

        Assert.True(queryStart >= 0 && queryEnd > queryStart);
        Assert.True(detailStart >= 0 && detailEnd > detailStart);
        var detailGraph = source[queryStart..queryEnd];
        var detailLoader = source[detailStart..detailEnd];

        Assert.Contains("ThenInclude(b => b.Slots).ThenInclude(s => s.Court)", detailGraph);
        Assert.Contains("public List<MatchBookingSlotResponse> BookingSlots", dto);
        Assert.Contains("BookingSlots = firstBooking?.Slots", detailLoader);
        Assert.Contains("CourtNumber = slot.Court.CourtNumber", detailLoader);
        Assert.Contains("StartTime = slot.StartTime", detailLoader);
        Assert.Contains("EndTime = slot.EndTime", detailLoader);
    }

    [Fact]
    public void MatchBookingUsesOneCheckInGroupForAdjacentSlotsOnTheSameCourt()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Open.cs"));
        var methodStart = source.IndexOf(" CreateMatchBooking(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf(" CancelPendingMatchBooking(", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        // Normalised: this asserts the grouping order, not which line ending the file happens to use.
        var method = source[methodStart..methodEnd].Replace("\r\n", "\n");

        Assert.Contains("foreach (var selectedSlot in parsedSlots\n            .OrderBy(slot => slot.CourtId)\n            .ThenBy(slot => slot.StartTime))", method);
        Assert.Contains("currentCheckInGroup.EndTime != selectedSlot.StartTime", method);
        Assert.Contains("booking.CheckInGroups.Add(currentCheckInGroup)", method);
    }

    [Fact]
    public void MatchRoomLoadsBookingHistoryInBoundedPages()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchController.Open.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var contract = File.ReadAllText(SourcePath("Services", "Matches", "IMatchService.cs"));
        var dto = File.ReadAllText(SourcePath("DTOs", "MatchRequest.cs"));

        Assert.Contains("booking-rounds", controller);
        Assert.Contains("GetOpenMatchBookingRounds", contract);
        Assert.Contains("InitialMatchBookingRoundsPageSize = 3", service);
        Assert.Contains("PopulateBookingRoundPageAsync", service);
        Assert.Contains("bookingCheckInGroupIds.Contains", service);
        Assert.Contains("BookingCheckInsTotalCount", dto);
        Assert.Contains("BookingCheckInsTotalPages", dto);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var cleanSegments = relativeSegments.FirstOrDefault() == "PicklinkBackend" ? relativeSegments[1..] : relativeSegments;
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

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
