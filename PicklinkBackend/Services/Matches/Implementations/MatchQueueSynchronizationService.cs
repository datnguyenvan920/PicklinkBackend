using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches.Implementations;

/// <summary>
/// Keeps a matchmaking ticket and its already-created room consistent. A queue
/// remains the discovery ticket; a match remains the room used for chat and booking.
/// </summary>
public sealed class MatchQueueSynchronizationService
{
    private readonly IMatchRepository _matchRepository;
    private readonly IFirebaseService? _firebaseService;

    public MatchQueueSynchronizationService(
        IMatchRepository matchRepository,
        IFirebaseService? firebaseService = null)
    {
        _matchRepository = matchRepository;
        _firebaseService = firebaseService;
    }

    public async Task<Match> CreateManualMatchForQueueAsync(
        MatchmakingQueue queue,
        CancellationToken cancellationToken)
    {
        if (!queue.IsPublic)
            throw new InvalidOperationException("Only public manual queues have a room created immediately.");

        if (queue.MatchId.HasValue)
        {
            var existing = await _matchRepository.Matches
                .Include(match => match.MatchParticipants)
                .SingleOrDefaultAsync(match => match.MatchId == queue.MatchId.Value, cancellationToken);
            if (existing is not null) return existing;
        }

        var queuePlayerIds = queue.QueuePlayers.Select(player => player.PlayerId).Distinct().ToList();
        var players = await _matchRepository.Players
            .Include(player => player.User)
            .Where(player => queuePlayerIds.Contains(player.PlayerId))
            .ToDictionaryAsync(player => player.PlayerId, cancellationToken);
        var approvedQueuePlayers = queue.QueuePlayers
            .Where(IsApproved)
            .ToList();
        var host = approvedQueuePlayers.FirstOrDefault(player => player.IsHost)
            ?? throw new InvalidOperationException("A manual queue must have a host before its room is created.");

        var today = DateOnly.FromDateTime(VietnamTime.Now);
        var specificDates = queue.QueueSlots
            .Where(slot => slot.SpecificDate.HasValue)
            .Select(slot => slot.SpecificDate!.Value)
            .OrderBy(date => date)
            .ToList();
        var availableFrom = specificDates.FirstOrDefault(today);
        var availableTo = specificDates.LastOrDefault(
            string.Equals(queue.ReplayType, "None", StringComparison.OrdinalIgnoreCase)
                ? availableFrom
                : availableFrom.AddDays(30));
        var distinctTimes = queue.QueueSlots
            .OrderBy(slot => slot.TimeStart)
            .ThenBy(slot => slot.TimeEnd)
            .DistinctBy(slot => (slot.TimeStart, slot.TimeEnd))
            .ToList();
        var now = DateTime.UtcNow;

        var match = new Match
        {
            HostPlayerId = host.PlayerId,
            MatchType = queue.MatchType,
            MatchSkillLevel = queue.SkillLevel,
            MinSkillLevel = queue.MinSkillLevel,
            MaxSkillLevel = queue.MaxSkillLevel,
            RequiredPlayerCount = queue.PlayerCount,
            Status = approvedQueuePlayers.Count >= queue.PlayerCount ? "ReadyToBook" : "Recruiting",
            Origin = "Manual",
            Title = queue.Title,
            Province = queue.Province ?? string.Empty,
            Ward = queue.Ward ?? string.Empty,
            SearchRadiusKm = queue.SearchRadiusKm,
            SearchLatitude = queue.SearchLatitude,
            SearchLongitude = queue.SearchLongitude,
            SharedVenues = queue.SharedVenues,
            ReplayType = queue.ReplayType,
            ReplayWeekdays = queue.ReplayWeekdays,
            AvailableDateFrom = availableFrom,
            AvailableDateTo = availableTo,
            PreferredTimeStart = distinctTimes.FirstOrDefault()?.TimeStart,
            PreferredTimeEnd = distinctTimes.LastOrDefault()?.TimeEnd,
            CreatedAt = now
        };

        foreach (var slot in distinctTimes)
        {
            match.AvailabilitySlots.Add(new MatchAvailabilitySlot
            {
                TimeStart = slot.TimeStart,
                TimeEnd = slot.TimeEnd
            });
        }

        foreach (var queuePlayer in queue.QueuePlayers)
        {
            match.MatchParticipants.Add(new MatchParticipant
            {
                PlayerId = queuePlayer.PlayerId,
                Status = NormalizeMatchStatus(queuePlayer.Status),
                IsHost = queuePlayer.IsHost,
                RequestedAt = now,
                RespondedAt = IsApproved(queuePlayer) ? now : null
            });
        }

        await _matchRepository.AddMatchAsync(match, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        var conversation = new Conversation
        {
            MatchId = match.MatchId,
            ConversationType = "LobbyChat",
            ConversationName = match.Title ?? $"Phòng #{match.MatchId}",
            CreatedAt = now
        };
        await _matchRepository.AddConversationAsync(conversation, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        foreach (var queuePlayer in approvedQueuePlayers)
        {
            if (!players.TryGetValue(queuePlayer.PlayerId, out var player)) continue;
            await _matchRepository.AddConversationParticipantAsync(new ConversationParticipant
            {
                ConversationId = conversation.ConversationId,
                UserId = player.UserId,
                JoinedAt = now
            }, cancellationToken);
        }

        queue.MatchId = match.MatchId;
        queue.IsActive = match.Status == "Recruiting";
        queue.UpdatedAt = now;
        return match;
    }

    public async Task<Match?> SyncQueuePlayerToMatchAsync(
        MatchmakingQueue queue,
        MatchmakingQueuePlayer queuePlayer,
        CancellationToken cancellationToken)
    {
        if (!queue.MatchId.HasValue) return null;

        var match = await _matchRepository.Matches
            .Include(item => item.MatchParticipants)
            .SingleOrDefaultAsync(item => item.MatchId == queue.MatchId.Value, cancellationToken);
        if (match is null) return null;

        var status = NormalizeMatchStatus(queuePlayer.Status);
        var participant = match.MatchParticipants.FirstOrDefault(item => item.PlayerId == queuePlayer.PlayerId);
        var now = DateTime.UtcNow;
        if (participant is null)
        {
            participant = new MatchParticipant
            {
                MatchId = match.MatchId,
                PlayerId = queuePlayer.PlayerId,
                Status = status,
                IsHost = queuePlayer.IsHost,
                RequestedAt = now,
                RespondedAt = IsApprovedStatus(status) || IsTerminalStatus(status) ? now : null
            };
            await _matchRepository.AddParticipantAsync(participant, cancellationToken);
            if (!match.MatchParticipants.Contains(participant)) match.MatchParticipants.Add(participant);
        }
        else
        {
            participant.Status = status;
            participant.IsHost = queuePlayer.IsHost;
            participant.RespondedAt = IsApprovedStatus(status) || IsTerminalStatus(status) ? now : null;
            if (!IsTerminalStatus(status)) participant.RequestedAt = now;
        }

        await SynchronizeConversationMembershipAsync(
            match.MatchId,
            "LobbyChat",
            queuePlayer.PlayerId,
            IsApprovedStatus(status),
            cancellationToken);
        RecalculateRecruitingState(match);
        queue.IsActive = match.Status == "Recruiting";
        queue.UpdatedAt = now;
        return match;
    }

    public async Task<MatchmakingQueue?> SyncMatchParticipantToQueueAsync(
        int matchId,
        MatchParticipant participant,
        CancellationToken cancellationToken)
    {
        var queues = await _matchRepository.MatchmakingQueues
            .Include(item => item.QueuePlayers)
            .Where(item => item.MatchId == matchId)
            .ToListAsync(cancellationToken);
        var primaryQueue = FindPrimaryQueue(queues);
        if (primaryQueue is null) return null;

        var status = NormalizeQueueStatus(participant.Status);
        var queuesToUpdate = queues
            .Where(item => item.QueuePlayers.Any(player => player.PlayerId == participant.PlayerId))
            .ToList();
        if (!queuesToUpdate.Contains(primaryQueue)) queuesToUpdate.Add(primaryQueue);

        var now = DateTime.UtcNow;
        foreach (var queue in queuesToUpdate)
        {
            var queuePlayer = queue.QueuePlayers.FirstOrDefault(item => item.PlayerId == participant.PlayerId);
            if (queuePlayer is null)
            {
                queuePlayer = new MatchmakingQueuePlayer
                {
                    MatchmakingQueueId = queue.MatchmakingQueueId,
                    PlayerId = participant.PlayerId,
                    IsHost = participant.IsHost,
                    Status = status
                };
                await _matchRepository.AddQueuePlayerAsync(queuePlayer, cancellationToken);
                if (!queue.QueuePlayers.Contains(queuePlayer)) queue.QueuePlayers.Add(queuePlayer);
            }
            else
            {
                queuePlayer.Status = status;
                queuePlayer.IsHost = participant.IsHost;
            }

            await SynchronizeConversationMembershipAsync(
                queue.MatchmakingQueueId,
                "QueueLobbyChat",
                participant.PlayerId,
                IsApprovedStatus(status),
                cancellationToken,
                isQueueConversation: true);
            var approvedCount = CountApproved(queue.QueuePlayers);
            queue.IsActive = queue == primaryQueue && approvedCount > 0 && approvedCount < queue.PlayerCount;
            queue.UpdatedAt = now;
        }

        return primaryQueue;
    }

    public async Task<MatchmakingQueue?> SyncMatchDetailsToQueueAsync(
        Match match,
        CancellationToken cancellationToken)
    {
        var queues = await _matchRepository.MatchmakingQueues
            .Include(item => item.QueueSlots)
            .Include(item => item.QueuePlayers)
            .Where(item => item.MatchId == match.MatchId)
            .ToListAsync(cancellationToken);
        var queue = FindPrimaryQueue(queues);
        if (queue is null) return null;

        queue.Title = match.Title ?? string.Empty;
        queue.PlayerCount = match.RequiredPlayerCount;
        queue.MatchType = match.MatchType;
        queue.SkillLevel = match.MatchSkillLevel;
        queue.MinSkillLevel = match.MinSkillLevel;
        queue.MaxSkillLevel = match.MaxSkillLevel;
        queue.SearchLatitude = match.SearchLatitude;
        queue.SearchLongitude = match.SearchLongitude;
        queue.SearchRadiusKm = match.SearchRadiusKm;
        queue.Province = match.Province;
        queue.Ward = match.Ward;
        queue.SharedVenues = match.SharedVenues;
        queue.UpdatedAt = DateTime.UtcNow;
        var approvedCount = match.MatchParticipants.Count(participant =>
            MatchRoomLifecyclePolicy.IsRoomMemberStatus(participant.Status));
        queue.IsActive = match.Status == "Recruiting" && approvedCount > 0 && approvedCount < queue.PlayerCount;

        var oldSlots = queue.QueueSlots.ToList();
        var monthlyDays = oldSlots.Where(slot => slot.DayOfMonth.HasValue)
            .Select(slot => slot.DayOfMonth!.Value).Distinct().ToList();
        await _matchRepository.RemoveRangeQueueSlotsAsync(oldSlots, cancellationToken);
        queue.QueueSlots.Clear();

        var availability = match.AvailabilitySlots.Count > 0
            ? match.AvailabilitySlots.Select(slot => (slot.TimeStart, slot.TimeEnd)).Distinct().ToList()
            : new List<(TimeOnly TimeStart, TimeOnly TimeEnd)>
            {
                (match.PreferredTimeStart ?? new TimeOnly(8, 0), match.PreferredTimeEnd ?? new TimeOnly(22, 0))
            };

        if (string.Equals(queue.ReplayType, "Weekly", StringComparison.OrdinalIgnoreCase))
        {
            var weekdays = (match.ReplayWeekdays ?? queue.ReplayWeekdays ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Enum.TryParse<DayOfWeek>(value, true, out var day) ? (DayOfWeek?)day : null)
                .Where(day => day.HasValue)
                .Select(day => day!.Value)
                .Distinct()
                .ToList();
            foreach (var day in weekdays)
            foreach (var slot in availability)
                queue.QueueSlots.Add(new MatchmakingQueueSlot { DayOfWeek = day, TimeStart = slot.TimeStart, TimeEnd = slot.TimeEnd });
        }
        else if (string.Equals(queue.ReplayType, "Monthly", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var day in monthlyDays)
            foreach (var slot in availability)
                queue.QueueSlots.Add(new MatchmakingQueueSlot { DayOfMonth = day, TimeStart = slot.TimeStart, TimeEnd = slot.TimeEnd });
        }
        else if (string.Equals(queue.ReplayType, "Daily", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var slot in availability)
                queue.QueueSlots.Add(new MatchmakingQueueSlot { TimeStart = slot.TimeStart, TimeEnd = slot.TimeEnd });
        }
        else
        {
            var from = match.AvailableDateFrom ?? DateOnly.FromDateTime(VietnamTime.Now);
            var to = match.AvailableDateTo ?? from;
            if (to.DayNumber - from.DayNumber > 30) to = from.AddDays(30);
            for (var date = from; date <= to; date = date.AddDays(1))
            foreach (var slot in availability)
                queue.QueueSlots.Add(new MatchmakingQueueSlot { SpecificDate = date, TimeStart = slot.TimeStart, TimeEnd = slot.TimeEnd });
        }

        return queue;
    }

    public async Task SyncQueueToFirebaseAsync(MatchmakingQueue? queue, CancellationToken cancellationToken)
    {
        if (queue is null || _firebaseService is null || !_firebaseService.IsConfigured) return;
        if (!queue.IsActive)
        {
            await _firebaseService.RemoveQueueAsync(queue.MatchmakingQueueId, cancellationToken);
            return;
        }
        await _firebaseService.SyncQueueAsync(queue.MatchmakingQueueId, new
        {
            queue.MatchmakingQueueId,
            queue.MatchId,
            queue.Title,
            queue.PlayerCount,
            queue.MatchType,
            queue.SkillLevel,
            queue.MinSkillLevel,
            queue.MaxSkillLevel,
            queue.SearchLatitude,
            queue.SearchLongitude,
            queue.SearchRadiusKm,
            queue.IsActive,
            queue.IsPublic,
            queue.Province,
            queue.Ward,
            queue.SharedVenues,
            queue.ReplayType,
            queue.ReplayWeekdays,
            UpdatedAt = queue.UpdatedAt.ToString("o"),
            CreatedAt = queue.CreatedAt.ToString("o")
        }, cancellationToken);
    }

    private async Task SynchronizeConversationMembershipAsync(
        int ownerId,
        string conversationType,
        int playerId,
        bool shouldBelong,
        CancellationToken cancellationToken,
        bool isQueueConversation = false)
    {
        var userId = await _matchRepository.Players
            .Where(player => player.PlayerId == playerId)
            .Select(player => (int?)player.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!userId.HasValue) return;

        var conversation = await _matchRepository.Conversations.FirstOrDefaultAsync(
            item => item.ConversationType == conversationType
                && (isQueueConversation ? item.MatchmakingQueueId == ownerId : item.MatchId == ownerId),
            cancellationToken);
        if (conversation is null) return;

        var membership = await _matchRepository.ConversationParticipants.FirstOrDefaultAsync(
            item => item.ConversationId == conversation.ConversationId && item.UserId == userId.Value,
            cancellationToken);
        if (shouldBelong && membership is null)
        {
            await _matchRepository.AddConversationParticipantAsync(new ConversationParticipant
            {
                ConversationId = conversation.ConversationId,
                UserId = userId.Value,
                JoinedAt = DateTime.UtcNow
            }, cancellationToken);
        }
        else if (!shouldBelong && membership is not null)
        {
            await _matchRepository.RemoveConversationParticipantAsync(membership, cancellationToken);
        }
    }

    private static void RecalculateRecruitingState(Match match)
    {
        if (match.Status is not ("Recruiting" or "ReadyToBook")) return;
        var approvedCount = match.MatchParticipants.Count(participant => IsApprovedStatus(participant.Status));
        if (approvedCount == 0)
        {
            match.HostPlayerId = null;
            match.Status = "Cancelled";
            match.CancelledAt ??= DateTime.UtcNow;
            return;
        }
        match.Status = approvedCount >= match.RequiredPlayerCount
            ? "ReadyToBook"
            : "Recruiting";
    }

    private static bool IsApproved(MatchmakingQueuePlayer player) =>
        player.IsHost || IsApprovedStatus(player.Status);

    private static int CountApproved(IEnumerable<MatchmakingQueuePlayer> players) =>
        players.Count(IsApproved);

    private static MatchmakingQueue? FindPrimaryQueue(IEnumerable<MatchmakingQueue> queues) =>
        queues
            .OrderByDescending(queue => CountApproved(queue.QueuePlayers))
            .ThenBy(queue => queue.MatchmakingQueueId)
            .FirstOrDefault();

    private static bool IsApprovedStatus(string? status) =>
        string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminalStatus(string status) =>
        status is "Rejected" or "Left" or "Removed";

    private static string NormalizeMatchStatus(string? status) =>
        string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase) ? "Approved" : status ?? "Pending";

    private static string NormalizeQueueStatus(string? status) =>
        string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase) ? "Approved" : status ?? "Pending";
}
