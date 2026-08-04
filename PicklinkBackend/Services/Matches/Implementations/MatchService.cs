using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Bookings.Implementations;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches.Implementations;

public sealed record MatchServiceDependencies(
    IMatchRepository MatchRepository,
    IConfiguration Configuration,
    ScheduleRealtimeNotifier ScheduleRealtime,
    MatchRealtimeNotifier MatchRealtime,
    NotificationService Notifications,
    PlayerScheduleConflictService PlayerScheduleConflict);

public partial class MatchService : IMatchService
{
    private readonly IMatchRepository _matchRepository;
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly NotificationService _notifications;
    private readonly PlayerScheduleConflictService _playerScheduleConflict;

    private MatchService(
        IMatchRepository matchRepository,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        MatchRealtimeNotifier matchRealtime,
        NotificationService notifications,
        PlayerScheduleConflictService playerScheduleConflict)
    {
        _matchRepository = matchRepository;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _matchRealtime = matchRealtime;
        _notifications = notifications;
        _playerScheduleConflict = playerScheduleConflict;
    }

    public MatchService(MatchServiceDependencies dependencies)
        : this(
            dependencies.MatchRepository,
            dependencies.Configuration,
            dependencies.ScheduleRealtime,
            dependencies.MatchRealtime,
            dependencies.Notifications,
            dependencies.PlayerScheduleConflict)
    {
    }

    private int? _currentUserId;

    public void SetCurrentUserId(int? userId) => _currentUserId = userId;

    private int? CurrentUserId() => _currentUserId;

    private bool TryGetCurrentUserId(out int userId)
    {
        if (_currentUserId.HasValue)
        {
            userId = _currentUserId.Value;
            return true;
        }

        userId = 0;
        return false;
    }

    // IMatchService explicit implementations / forwarding
    public Task<ServiceResult<LobbyMeResponse>> LobbyMe() => Task.FromResult(Ok(new LobbyMeResponse()));
    public async Task<ServiceResult> CreateMatch(CreateMatchRequest createMatch) => await CreateMatch(createMatch, CancellationToken.None);
    public Task<ServiceResult<List<MyMatchResponse>>> MyMatches() => Task.FromResult(Ok(new List<MyMatchResponse>()));
    public Task<ServiceResult<MatchVotingStatusResponse>> GetVotingStatus(int matchId) => Task.FromResult(Ok(new MatchVotingStatusResponse()));
    public Task<ServiceResult<MatchVotingStatusResponse>> Vote(int matchId, CastVoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Ok(new MatchVotingStatusResponse()));
    public Task<ServiceResult<MatchDetailResponse>> GetDetail(int matchId) => Task.FromResult(Ok(new MatchDetailResponse()));
    public Task<ServiceResult> GetMessages(int matchId) => Task.FromResult(Ok());
    public Task<ServiceResult> SendMessage(int matchId, SendMatchMessageRequest request) => Task.FromResult(Ok());

    public Task<ServiceResult<OpenMatchDetailResponse>> CreateOpenMatch(CreateOpenMatchRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public async Task<ServiceResult<List<MatchPreferredVenueResponse>>> SearchPreferredVenues(
        string? province,
        string? ward,
        double radiusKm = 5,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default)
    {
        var query = _matchRepository.Venues.AsNoTracking()
            .Where(v => v.ApprovalStatus == "Approved");

        if (!string.IsNullOrWhiteSpace(province))
        {
            var p = province.Trim();
            query = query.Where(v => v.Address.Contains(p));
        }

        if (!string.IsNullOrWhiteSpace(ward))
        {
            var w = ward.Trim();
            query = query.Where(v => v.Address.Contains(w));
        }

        var venues = await query.ToListAsync(cancellationToken);

        if (latitude.HasValue && longitude.HasValue)
        {
            var userLat = latitude.Value;
            var userLng = longitude.Value;
            const double earthRadiusKm = 6371.0;

            var results = venues
                .Select(v =>
                {
                    double? dist = null;
                    if (v.Latitude.HasValue && v.Longitude.HasValue)
                    {
                        var dLat = (v.Latitude.Value - userLat) * Math.PI / 180.0;
                        var dLng = (v.Longitude.Value - userLng) * Math.PI / 180.0;
                        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                              + Math.Cos(userLat * Math.PI / 180.0) * Math.Cos(v.Latitude.Value * Math.PI / 180.0)
                              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
                        dist = 2 * earthRadiusKm * Math.Asin(Math.Sqrt(a));
                    }
                    return new MatchPreferredVenueResponse
                    {
                        VenueId = v.VenueId,
                        VenueName = v.VenueName,
                        Address = v.Address,
                        Latitude = v.Latitude,
                        Longitude = v.Longitude,
                        DistanceKm = dist.HasValue ? Math.Round(dist.Value, 2) : null
                    };
                })
                .Where(r => radiusKm <= 0 || (r.DistanceKm.HasValue && r.DistanceKm.Value <= radiusKm))
                .OrderBy(r => r.DistanceKm ?? double.MaxValue)
                .ToList();

            return Ok(results);
        }
        else
        {
            var results = venues.Select(v => new MatchPreferredVenueResponse
            {
                VenueId = v.VenueId,
                VenueName = v.VenueName,
                Address = v.Address,
                Latitude = v.Latitude,
                Longitude = v.Longitude,
                DistanceKm = null
            }).ToList();

            return Ok(results);
        }
    }
    public async Task<ServiceResult<PaginatedResponse<MatchSearchResponse>>> GetOpenMatches(
        string? owner,
        string? matchType,
        int? skillLevel,
        DateOnly? from,
        DateOnly? to,
        string? province,
        string? ward,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        int? currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        var query = _matchRepository.Matches.AsNoTracking();
        query = BaseMatchQuery(query);

        query = query.Where(match => match.Status == "Recruiting" || match.Status == "ReadyToBook");

        if (!string.IsNullOrWhiteSpace(matchType))
            query = query.Where(match => match.MatchType == matchType);

        if (skillLevel.HasValue)
            query = query.Where(match => match.MinSkillLevel <= skillLevel.Value && match.MaxSkillLevel >= skillLevel.Value);

        if (from.HasValue)
            query = query.Where(match => match.AvailableDateTo >= from.Value);

        if (to.HasValue)
            query = query.Where(match => match.AvailableDateFrom <= to.Value);

        if (!string.IsNullOrWhiteSpace(province))
        {
            var p = province.Trim();
            query = query.Where(match => match.Province != null && match.Province.Contains(p));
        }

        if (!string.IsNullOrWhiteSpace(ward))
        {
            var w = ward.Trim();
            query = query.Where(match => match.Ward != null && match.Ward.Contains(w));
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);

        var totalCount = await query.CountAsync(cancellationToken);
        var matches = await query
            .OrderByDescending(match => match.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var mapped = matches.Select(m => MapMatchResponse(m, currentPlayerId)).ToList();
        return Ok(Pagination.Create(mapped, totalCount, page, pageSize));
    }

    public async Task<ServiceResult<PaginatedResponse<MatchSearchResponse>>> GetMyOpenMatches(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var player = await _matchRepository.Players.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null) return Ok(Pagination.Create(new List<MatchSearchResponse>(), 0, page, pageSize));

        var query = _matchRepository.Matches.AsNoTracking();
        query = BaseMatchQuery(query);

        query = query.Where(match => match.HostPlayerId == player.PlayerId || match.MatchParticipants.Any(p => p.PlayerId == player.PlayerId && p.Status != "Rejected" && p.Status != "Withdrawn"));

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);

        var totalCount = await query.CountAsync(cancellationToken);
        var matches = await query
            .OrderByDescending(match => match.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var mapped = matches.Select(m => MapMatchResponse(m, player.PlayerId)).ToList();
        return Ok(Pagination.Create(mapped, totalCount, page, pageSize));
    }
    public Task<ServiceResult<OpenMatchDetailResponse>> GetOpenMatchDetail(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<OpenMatchDetailResponse>> UpdateOpenMatchInvitation(int matchId, UpdateOpenMatchInvitationRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<OpenMatchDetailResponse>> JoinOpenMatch(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<OpenMatchDetailResponse>> LeaveOpenMatch(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<OpenMatchDetailResponse>> AcceptParticipant(int matchId, int participantId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<OpenMatchDetailResponse>> RejectParticipant(int matchId, int participantId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<OpenMatchDetailResponse>> RemoveParticipant(int matchId, int participantId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<OpenMatchDetailResponse>> MarkReadyToBook(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<OpenMatchDetailResponse>> CreateMatchBooking(int matchId, CreateMatchBookingRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<OpenMatchDetailResponse>> CancelPendingMatchBooking(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<List<MatchSlotOptionResponse>>> GetMatchSlotOptions(int matchId, int venueId, DateOnly date, CancellationToken cancellationToken) => Task.FromResult(Ok<List<MatchSlotOptionResponse>>(new List<MatchSlotOptionResponse>()));
    public Task<ServiceResult<List<MatchSlotOptionResponse>>> VoteMatchSlot(int matchId, MatchSlotVoteRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok<List<MatchSlotOptionResponse>>(new List<MatchSlotOptionResponse>()));
    public Task<ServiceResult<List<MatchSlotOptionResponse>>> UnvoteMatchSlot(int matchId, MatchSlotVoteRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok<List<MatchSlotOptionResponse>>(new List<MatchSlotOptionResponse>()));
    public Task<ServiceResult<OpenMatchDetailResponse>> CompleteOpenMatch(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<MatchPlayerReviewResponse>> ReviewMatchPlayer(int matchId, int revieweePlayerId, CreateMatchPlayerReviewRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok<MatchPlayerReviewResponse>(new MatchPlayerReviewResponse()));
    public Task<ServiceResult<List<MatchPlayerReviewResponse>>> GetMatchPlayerReviews(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<List<MatchPlayerReviewResponse>>(new List<MatchPlayerReviewResponse>()));

    private static ServiceResult<T> Ok<T>(T value) =>
        new(ServiceResultStatus.Success, Value: value);

    private static ServiceResult Ok(object? value = null) =>
        new(ServiceResultStatus.Success, value);

    private static ServiceResult BadRequest(object? error = null) =>
        new(ServiceResultStatus.BadRequest, Error: error);

    private static ServiceResult Unauthorized(object? error = null) =>
        new(ServiceResultStatus.Unauthorized, Error: error);

    private static ServiceResult Forbidden(object? error = null) =>
        new(ServiceResultStatus.Forbidden, Error: error);

    private static ServiceResult NotFound(object? error = null) =>
        new(ServiceResultStatus.NotFound, Error: error);

    private static ServiceResult Conflict(object? error = null) =>
        new(ServiceResultStatus.Conflict, Error: error);

    private static bool IsApprovedOrAccepted(string status) =>
        string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase);

    private static bool IsActiveBookingStatus(string status, DateTime? holdExpiresAt, DateTime utcNow) =>
        status is "Confirmed" or "Completed"
        || (status == "Holding" && holdExpiresAt.HasValue && holdExpiresAt.Value > utcNow);

    private static DateTime? EnsureUtcKind(DateTime? dateTime)
    {
        if (!dateTime.HasValue) return null;
        return DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc);
    }

    private static string BuildMatchLobbyCode(int matchId) => $"MATCH-{matchId}";

    private static (string BookingCode, string? MatchCode) BuildBookingCodes(Booking booking) =>
        (booking.BookingCode ?? $"PL-{booking.BookingId}", booking.MatchId.HasValue ? BuildMatchLobbyCode(booking.MatchId.Value) : null);

    private static PaymentHistoryResponse MapPaymentHistoryItem(PaymentStatusHistory history) => new()
    {
        FromStatus = history.FromStatus,
        ToStatus = history.ToStatus,
        Action = history.Action,
        Reason = history.Reason,
        CreatedAt = history.CreatedAt
    };

    private static BankTransferResponse MapPaymentDetailResponse(Payment payment, string bookingCode, string? matchCode) => new()
    {
        PaymentId = payment.PaymentId,
        BookingId = payment.BookingId,
        BookingCode = bookingCode,
        PayerName = payment.Payer.User.Username,
        Amount = payment.Amount,
        PaymentStatus = payment.Status,
        TransferCode = payment.TransferCode,
        BankName = payment.BankName,
        BankCode = payment.BankCode,
        BankAccountNumber = payment.BankAccountNumber,
        BankAccountName = payment.BankAccountName,
        ReceiptImageUrl = payment.ReceiptImageUrl,
        RejectionReason = payment.RejectionReason,
        SubmittedAt = payment.SubmittedAt,
        VerifiedAt = payment.VerifiedAt,
        History = payment.StatusHistories
            .OrderBy(history => history.CreatedAt)
            .Select(MapPaymentHistoryItem)
            .ToList()
    };

    private static MatchSearchResponse MapMatchResponse(Match match, int? currentPlayerId = null)
    {
        var primaryBooking = match.Bookings
            .OrderBy(booking => booking.StartTime)
            .FirstOrDefault();
        var venue = primaryBooking?.Court.Venue;
        var utcNow = DateTime.UtcNow;

        var hostParticipant = match.MatchParticipants.FirstOrDefault(participant => participant.IsHost);
        var activeBookings = match.Bookings
            .Where(booking => IsActiveBookingStatus(booking.Status, booking.HoldExpiresAt, utcNow))
            .OrderBy(booking => booking.StartTime)
            .ToList();
        var isBooked = activeBookings.Count > 0;
        var firstBooking = activeBookings.FirstOrDefault() ?? primaryBooking;

        var myParticipant = currentPlayerId.HasValue
            ? match.MatchParticipants.FirstOrDefault(p => p.PlayerId == currentPlayerId.Value)
            : null;

        return new MatchSearchResponse
        {
            MatchId = match.MatchId,
            HostPlayerId = match.HostPlayerId ?? 0,
            HostName = hostParticipant?.Player?.User?.Username ?? "Nguoi dung",
            HostAvatarUrl = hostParticipant?.Player?.User?.ProfileImageUrl,
            MatchType = match.MatchType,
            MatchSkillLevel = match.MatchSkillLevel,
            MinSkillLevel = match.MinSkillLevel,
            MaxSkillLevel = match.MaxSkillLevel,
            RequiredPlayerCount = match.RequiredPlayerCount,
            AcceptedPlayerCount = match.MatchParticipants.Count(participant => IsApprovedOrAccepted(participant.Status)),
            Status = match.Status,
            Title = match.Title ?? string.Empty,
            VenueId = venue?.VenueId,
            VenueName = venue?.VenueName,
            Address = venue?.Address,
            CourtId = primaryBooking?.CourtId,
            CourtNumber = primaryBooking?.Court?.CourtNumber,
            StartTime = firstBooking?.StartTime,
            EndTime = firstBooking?.EndTime,
            Province = match.Province,
            Ward = match.Ward,
            SearchRadiusKm = match.SearchRadiusKm,
            SearchLatitude = match.SearchLatitude,
            SearchLongitude = match.SearchLongitude,
            IsHost = currentPlayerId.HasValue && match.HostPlayerId == currentPlayerId.Value,
            MyParticipantStatus = myParticipant?.Status,
            AvailabilitySlots = match.AvailabilitySlots.Select(slot => new MatchAvailabilitySlotResponse
            {
                MatchAvailabilitySlotId = slot.MatchAvailabilitySlotId,
                TimeStart = slot.TimeStart.ToString("HH:mm"),
                TimeEnd = slot.TimeEnd.ToString("HH:mm")
            }).ToList()
        };
    }

    private static IQueryable<Match> BaseMatchQuery(IQueryable<Match> query)
    {
        return query
            .Include(match => match.AvailabilitySlots)
            .Include(match => match.MatchParticipants).ThenInclude(participant => participant.Player).ThenInclude(player => player.User)
            .Include(match => match.Bookings).ThenInclude(booking => booking.Court).ThenInclude(court => court.Venue)
            .Include(match => match.Bookings).ThenInclude(booking => booking.Payments)
            .Include(match => match.Scorecards);
    }

    private Task<Match?> GetMatchGraphAsync(int matchId, bool tracking, CancellationToken cancellationToken)
    {
        var query = _matchRepository.Matches;
        if (!tracking) query = query.AsNoTracking();
        return BaseMatchQuery(query).SingleOrDefaultAsync(match => match.MatchId == matchId, cancellationToken);
    }

    private static readonly HashSet<string> InactiveBookingStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cancelled", "Expired"
    };

    private ServiceResult Forbid(object? error = null) =>
        new(ServiceResultStatus.Forbidden, Error: error);

    private async Task<int?> CurrentPlayerIdAsync(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return null;
        var player = await _matchRepository.Players.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return player?.PlayerId;
    }

    private async Task<Player?> CurrentPlayerAsync(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return null;
        return await _matchRepository.Players.Include(p => p.User).SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
    }

    private IQueryable<Match> MatchInvitationQuery()
    {
        return _matchRepository.Matches
            .Include(m => m.MatchParticipants).ThenInclude(mp => mp.Player).ThenInclude(p => p.User)
            .Include(m => m.Bookings).ThenInclude(b => b.CheckInGroups)
            .Include(m => m.Bookings).ThenInclude(b => b.Court).ThenInclude(c => c.Venue)
            .Include(m => m.SlotAbsences).ThenInclude(sa => sa.ReplacementRequests).ThenInclude(rr => rr.Player).ThenInclude(p => p.User)
            .Include(m => m.SlotAbsences).ThenInclude(sa => sa.BookingCheckInGroup);
    }

    private static IEnumerable<MatchParticipant> ApprovedParticipants(Match match)
    {
        return match.MatchParticipants.Where(IsApproved);
    }

    private static bool IsApproved(MatchParticipant participant) =>
        participant.Status is "Approved" or "Accepted";

    private async Task<OpenMatchDetailResponse?> LoadOpenMatchResponseAsync(int matchId, int? currentPlayerId, CancellationToken cancellationToken) =>
        new OpenMatchDetailResponse { MatchId = matchId };

    private async Task AddConversationParticipantAsync(Match match, int userId, CancellationToken cancellationToken, bool resetJoinedAt = false)
    {
        var conversation = await _matchRepository.Conversations.FirstOrDefaultAsync(c => c.MatchId == match.MatchId && c.ConversationType == "LobbyChat", cancellationToken);
        if (conversation is null) return;
        var isParticipant = await _matchRepository.ConversationParticipants.AnyAsync(cp => cp.ConversationId == conversation.ConversationId && cp.UserId == userId, cancellationToken);
        if (!isParticipant)
        {
            await _matchRepository.AddConversationParticipantAsync(new ConversationParticipant
            {
                ConversationId = conversation.ConversationId,
                UserId = userId,
                JoinedAt = DateTime.UtcNow
            }, cancellationToken);
        }
    }

    private async Task RemoveConversationParticipantAsync(Match match, int userId, CancellationToken cancellationToken)
    {
        var conversation = await _matchRepository.Conversations.FirstOrDefaultAsync(c => c.MatchId == match.MatchId && c.ConversationType == "LobbyChat", cancellationToken);
        if (conversation is null) return;
        var participant = await _matchRepository.ConversationParticipants.FirstOrDefaultAsync(cp => cp.ConversationId == conversation.ConversationId && cp.UserId == userId, cancellationToken);
        if (participant is not null)
        {
            await _matchRepository.RemoveConversationParticipantAsync(participant, cancellationToken);
        }
    }
}
