using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Bookings.Implementations;
using PicklinkBackend.Services.Community;
using PicklinkBackend.Services.Community.Implementations;
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
    PlayerScheduleConflictService PlayerScheduleConflict,
    CommunityDirectConversationService DirectConversations);

public partial class MatchService : IMatchService
{
    private readonly IMatchRepository _matchRepository;
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly NotificationService _notifications;
    private readonly PlayerScheduleConflictService _playerScheduleConflict;
    private readonly CommunityDirectConversationService _directConversations;

    private MatchService(
        IMatchRepository matchRepository,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        MatchRealtimeNotifier matchRealtime,
        NotificationService notifications,
        PlayerScheduleConflictService playerScheduleConflict,
        CommunityDirectConversationService directConversations)
    {
        _matchRepository = matchRepository;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _matchRealtime = matchRealtime;
        _notifications = notifications;
        _playerScheduleConflict = playerScheduleConflict;
        _directConversations = directConversations;
    }

    public MatchService(MatchServiceDependencies dependencies)
        : this(
            dependencies.MatchRepository,
            dependencies.Configuration,
            dependencies.ScheduleRealtime,
            dependencies.MatchRealtime,
            dependencies.Notifications,
            dependencies.PlayerScheduleConflict,
            dependencies.DirectConversations)
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
    public async Task<ServiceResult> GetMessages(int matchId, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var conversationId = await FindMatchConversationIdAsync(matchId, cancellationToken);
        if (!conversationId.HasValue)
            return NotFound(new { message = "Không tìm thấy phòng chat của trận." });

        var result = await _directConversations.GetDirectMessagesAsync(
            userId,
            conversationId.Value,
            beforeMessageId: null,
            limit: 50,
            cancellationToken);

        return MapDirectConversationResult(result);
    }

    public async Task<ServiceResult> SendMessage(
        int matchId,
        SendMatchMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var conversationId = await FindMatchConversationIdAsync(matchId, cancellationToken);
        if (!conversationId.HasValue)
            return NotFound(new { message = "Không tìm thấy phòng chat của trận." });

        var result = await _directConversations.SendDirectMessageAsync(
            userId,
            conversationId.Value,
            new SendCommunityMessageRequest(request.Content, MediaUrl: null, ReplyToMessageId: null),
            cancellationToken);

        if (result.IsSuccess) _matchRealtime.Publish(matchId, "MessageSent");
        return MapDirectConversationResult(result);
    }

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
        string? source,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        int? currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        var query = _matchRepository.Matches.AsNoTracking();
        query = BaseMatchQuery(query);

        query = query.Where(match =>
            match.Status == "Recruiting" &&
            match.MatchParticipants.Count(participant =>
                participant.Status == "Approved" || participant.Status == "Accepted") < match.RequiredPlayerCount);

        if (currentPlayerId.HasValue)
        {
            var playerId = currentPlayerId.Value;
            query = query.Where(match =>
                match.HostPlayerId != playerId &&
                !match.MatchParticipants.Any(participant =>
                    participant.PlayerId == playerId &&
                    (participant.Status == "Pending" ||
                     participant.Status == "Approved" ||
                     participant.Status == "Accepted")));
        }

        if (string.Equals(source, "manual", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(match => match.Origin == "Manual");
        }
        else if (string.Equals(source, "community", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(match => match.Origin == "Community");
        }

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

        var preferredVenueLookup = await LoadPreferredVenueLookupAsync(matches, cancellationToken);
        var mapped = matches.Select(m => MapMatchResponse(
            m,
            currentPlayerId,
            PreferredVenuesFor(m, preferredVenueLookup))).ToList();
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

        query = query.Where(match =>
            match.HostPlayerId == player.PlayerId ||
            match.MatchParticipants.Any(participant =>
                participant.PlayerId == player.PlayerId &&
                (participant.Status == "Pending" ||
                 participant.Status == "Approved" ||
                 participant.Status == "Accepted")));

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);

        var totalCount = await query.CountAsync(cancellationToken);
        var matches = await query
            .OrderByDescending(match => match.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var preferredVenueLookup = await LoadPreferredVenueLookupAsync(matches, cancellationToken);
        var mapped = matches.Select(m => MapMatchResponse(
            m,
            player.PlayerId,
            PreferredVenuesFor(m, preferredVenueLookup))).ToList();
        return Ok(Pagination.Create(mapped, totalCount, page, pageSize));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> GetOpenMatchDetail(int matchId, CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        var response = await LoadOpenMatchResponseAsync(matchId, playerId, cancellationToken);
        if (response is null) return NotFound(new { message = "Không tìm thấy trận đấu." });
        return Ok(response);
    }
    public Task<ServiceResult<OpenMatchDetailResponse>> UpdateOpenMatchInvitation(int matchId, UpdateOpenMatchInvitationRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public async Task<ServiceResult<OpenMatchDetailResponse>> JoinOpenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var player = await CurrentPlayerAsync(cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách người chơi đang được cập nhật." });

        var match = await MatchInvitationQuery()
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null)
            return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        if (match.HostPlayerId == player.PlayerId)
            return Conflict(new { message = "Bạn là chủ phòng ghép trận." });
        if (match.Status != "Recruiting")
            return Conflict(new { message = "Phòng hiện không nhận thêm yêu cầu tham gia." });
        if (ApprovedParticipants(match).Count() >= match.RequiredPlayerCount)
            return Conflict(new { message = "Phòng đã đủ người." });
        if (player.SkillLevel < match.MinSkillLevel || player.SkillLevel > match.MaxSkillLevel)
            return Conflict(new
            {
                message = $"Trình độ của bạn chưa nằm trong khoảng {match.MinSkillLevel}–{match.MaxSkillLevel} của lời mời."
            });

        var participant = match.MatchParticipants
            .SingleOrDefault(item => item.PlayerId == player.PlayerId);
        if (participant?.Status is "Approved" or "Accepted" or "Pending")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Ok((await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken))!);
        }

        var requestedAt = DateTime.UtcNow;
        if (participant is null)
        {
            participant = new MatchParticipant
            {
                MatchId = match.MatchId,
                PlayerId = player.PlayerId,
                Status = "Pending",
                IsHost = false,
                RequestedAt = requestedAt
            };
            await _matchRepository.AddParticipantAsync(participant, cancellationToken);
        }
        else
        {
            participant.Status = "Pending";
            participant.IsHost = false;
            participant.RequestedAt = requestedAt;
            participant.RespondedAt = null;
        }

        var host = match.MatchParticipants
            .FirstOrDefault(item => item.PlayerId == match.HostPlayerId)?.Player;
        if (host is not null)
        {
            _notifications.Add(new NotificationInput(
                UserId: host.UserId,
                Type: NotificationTypes.Match,
                Title: "Yêu cầu tham gia trận đấu",
                Message: $"{player.User.Username} muốn tham gia trận \"{match.Title ?? $"Phòng #{match.MatchId}"}\".",
                Tone: NotificationTones.Info,
                LinkTo: $"/matches/{match.MatchId}",
                LinkLabel: "Xem yêu cầu"));
        }

        await _matchRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        _matchRealtime.Publish(matchId, "JoinRequested");
        return Ok((await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken))!);
    }
    public Task<ServiceResult<OpenMatchDetailResponse>> LeaveOpenMatch(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public async Task<ServiceResult<OpenMatchDetailResponse>> AcceptParticipant(
        int matchId,
        int participantId,
        CancellationToken cancellationToken)
    {
        var approverPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (approverPlayerId is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách người chơi đang được cập nhật." });

        var match = await MatchInvitationQuery()
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null)
            return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        if (!ApprovedParticipants(match).Any(item => item.PlayerId == approverPlayerId.Value))
            return Forbid();
        if (match.Status != "Recruiting")
            return Conflict(new { message = "Chỉ có thể duyệt thành viên khi phòng đang tuyển người." });

        var participant = match.MatchParticipants
            .SingleOrDefault(item => item.ParticipantId == participantId);
        if (participant is null || participant.Status != "Pending")
            return Conflict(new { message = "Yêu cầu tham gia không còn ở trạng thái chờ duyệt." });
        if (ApprovedParticipants(match).Count() >= match.RequiredPlayerCount)
            return Conflict(new { message = "Phòng đã đủ số người cần thiết." });
        if (participant.Player.SkillLevel < match.MinSkillLevel || participant.Player.SkillLevel > match.MaxSkillLevel)
            return Conflict(new { message = "Trình độ người chơi không còn phù hợp với lời mời." });

        participant.Status = "Approved";
        participant.RespondedAt = DateTime.UtcNow;
        await AddConversationParticipantAsync(match, participant.Player.UserId, cancellationToken);
        _notifications.Add(new NotificationInput(
            UserId: participant.Player.UserId,
            Type: NotificationTypes.Match,
            Title: "Yêu cầu tham gia đã được duyệt",
            Message: $"Bạn đã được duyệt tham gia trận \"{match.Title ?? $"Phòng #{match.MatchId}"}\".",
            Tone: NotificationTones.Success,
            LinkTo: $"/matches/{match.MatchId}",
            LinkLabel: "Xem trận"));

        await _matchRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        _matchRealtime.Publish(matchId, "ParticipantApproved");
        return Ok((await LoadOpenMatchResponseAsync(matchId, approverPlayerId, cancellationToken))!);
    }

    public async Task<ServiceResult<OpenMatchDetailResponse>> RejectParticipant(
        int matchId,
        int participantId,
        CancellationToken cancellationToken)
    {
        var approverPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (approverPlayerId is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách người chơi đang được cập nhật." });

        var match = await MatchInvitationQuery()
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null)
            return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        if (!ApprovedParticipants(match).Any(item => item.PlayerId == approverPlayerId.Value))
            return Forbid();
        if (match.Status != "Recruiting")
            return Conflict(new { message = "Không thể xử lý yêu cầu sau khi phòng đã chuyển sang đặt sân." });

        var participant = match.MatchParticipants
            .SingleOrDefault(item => item.ParticipantId == participantId);
        if (participant is null || participant.Status != "Pending")
            return Conflict(new { message = "Yêu cầu tham gia không còn ở trạng thái chờ duyệt." });

        participant.Status = "Rejected";
        participant.RespondedAt = DateTime.UtcNow;
        _notifications.Add(new NotificationInput(
            UserId: participant.Player.UserId,
            Type: NotificationTypes.Match,
            Title: "Yêu cầu tham gia bị từ chối",
            Message: $"Yêu cầu tham gia trận \"{match.Title ?? $"Phòng #{match.MatchId}"}\" của bạn đã bị từ chối.",
            Tone: NotificationTones.Default,
            LinkTo: $"/matches/{match.MatchId}",
            LinkLabel: "Xem trận"));

        await _matchRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        _matchRealtime.Publish(matchId, "ParticipantRejected");
        return Ok((await LoadOpenMatchResponseAsync(matchId, approverPlayerId, cancellationToken))!);
    }
    public Task<ServiceResult<OpenMatchDetailResponse>> RemoveParticipant(int matchId, int participantId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<OpenMatchDetailResponse>> CancelPendingMatchBooking(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<List<MatchSlotOptionResponse>>> GetMatchSlotOptions(int matchId, int venueId, DateOnly date, CancellationToken cancellationToken) => Task.FromResult(Ok<List<MatchSlotOptionResponse>>(new List<MatchSlotOptionResponse>()));
    public Task<ServiceResult<List<MatchSlotOptionResponse>>> VoteMatchSlot(int matchId, MatchSlotVoteRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok<List<MatchSlotOptionResponse>>(new List<MatchSlotOptionResponse>()));
    public Task<ServiceResult<List<MatchSlotOptionResponse>>> UnvoteMatchSlot(int matchId, MatchSlotVoteRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok<List<MatchSlotOptionResponse>>(new List<MatchSlotOptionResponse>()));
    public Task<ServiceResult<OpenMatchDetailResponse>> CompleteOpenMatch(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<OpenMatchDetailResponse>(new OpenMatchDetailResponse()));
    public Task<ServiceResult<MatchPlayerReviewResponse>> ReviewMatchPlayer(int matchId, int revieweePlayerId, CreateMatchPlayerReviewRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok<MatchPlayerReviewResponse>(new MatchPlayerReviewResponse()));
    public Task<ServiceResult<List<MatchPlayerReviewResponse>>> GetMatchPlayerReviews(int matchId, CancellationToken cancellationToken) => Task.FromResult(Ok<List<MatchPlayerReviewResponse>>(new List<MatchPlayerReviewResponse>()));

    private Task<int?> FindMatchConversationIdAsync(int matchId, CancellationToken cancellationToken) =>
        _matchRepository.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.MatchId == matchId && conversation.ConversationType == "LobbyChat")
            .Select(conversation => (int?)conversation.ConversationId)
            .SingleOrDefaultAsync(cancellationToken);

    private static ServiceResult MapDirectConversationResult<T>(DirectConversationServiceResult<T> result) =>
        result.Status switch
        {
            DirectConversationServiceResultStatus.Success => new(ServiceResultStatus.Success, result.Value),
            DirectConversationServiceResultStatus.Created => new(ServiceResultStatus.Created, result.Value),
            DirectConversationServiceResultStatus.BadRequest => BadRequest(new
            {
                message = result.ErrorMessage ?? "Nội dung tin nhắn không hợp lệ."
            }),
            DirectConversationServiceResultStatus.Unauthorized => Unauthorized(),
            DirectConversationServiceResultStatus.Forbidden => Forbidden(),
            DirectConversationServiceResultStatus.NotFound => NotFound(new
            {
                message = result.ErrorMessage ?? "Không tìm thấy cuộc trò chuyện."
            }),
            DirectConversationServiceResultStatus.Conflict => Conflict(result.ErrorBody),
            _ => new ServiceResult(ServiceResultStatus.StatusCode, Error: result.ErrorBody, RawStatusCode: StatusCodes.Status500InternalServerError)
        };

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

    private static List<int> PreferredVenueIds(Match match) => !string.IsNullOrWhiteSpace(match.SharedVenues)
        ? match.SharedVenues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var venueId) ? venueId : 0)
            .Where(venueId => venueId > 0)
            .Distinct()
            .ToList()
        : [];

    private async Task<Dictionary<int, MatchPreferredVenueResponse>> LoadPreferredVenueLookupAsync(
        IEnumerable<Match> matches,
        CancellationToken cancellationToken)
    {
        var venueIds = matches.SelectMany(PreferredVenueIds).Distinct().ToList();
        if (venueIds.Count == 0) return [];

        return await _matchRepository.Venues
            .AsNoTracking()
            .Where(venue => venueIds.Contains(venue.VenueId))
            .Select(venue => new MatchPreferredVenueResponse
            {
                VenueId = venue.VenueId,
                VenueName = venue.VenueName,
                Address = venue.Address,
                Latitude = venue.Latitude,
                Longitude = venue.Longitude
            })
            .ToDictionaryAsync(venue => venue.VenueId, cancellationToken);
    }

    private static List<MatchPreferredVenueResponse> PreferredVenuesFor(
        Match match,
        IReadOnlyDictionary<int, MatchPreferredVenueResponse> venueLookup) => PreferredVenueIds(match)
            .Where(venueLookup.ContainsKey)
            .Select(venueId => venueLookup[venueId])
            .ToList();

    private static MatchSearchResponse MapMatchResponse(
        Match match,
        int? currentPlayerId = null,
        List<MatchPreferredVenueResponse>? preferredVenues = null)
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
        var acceptedPlayerCount = match.MatchParticipants.Count(participant => IsApprovedOrAccepted(participant.Status));
        var availableSlotCount = Math.Max(0, match.RequiredPlayerCount - acceptedPlayerCount);

        return new MatchSearchResponse
        {
            MatchId = match.MatchId,
            HostPlayerId = match.HostPlayerId ?? 0,
            HostName = hostParticipant?.Player?.User?.Username ?? "Người dùng",
            HostAvatarUrl = hostParticipant?.Player?.User?.ProfileImageUrl,
            MatchType = match.MatchType,
            MatchSkillLevel = match.MatchSkillLevel,
            MinSkillLevel = match.MinSkillLevel,
            MaxSkillLevel = match.MaxSkillLevel,
            RequiredPlayerCount = match.RequiredPlayerCount,
            NeededPlayerCount = availableSlotCount,
            AcceptedPlayerCount = acceptedPlayerCount,
            PendingRequestCount = match.MatchParticipants.Count(participant => participant.Status == "Pending"),
            AvailableSlotCount = availableSlotCount,
            Status = match.Status,
            Title = match.Title ?? string.Empty,
            Note = match.Note,
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
            AvailableDateFrom = match.AvailableDateFrom.GetValueOrDefault(),
            AvailableDateTo = match.AvailableDateTo.GetValueOrDefault(),
            PreferredTimeStart = match.PreferredTimeStart?.ToString("HH:mm") ?? string.Empty,
            PreferredTimeEnd = match.PreferredTimeEnd?.ToString("HH:mm") ?? string.Empty,
            PreferredVenues = preferredVenues ?? [],
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
            .Include(m => m.Bookings).ThenInclude(b => b.CheckInGroups).ThenInclude(g => g.Court)
            .Include(m => m.Bookings).ThenInclude(b => b.Payments)
            .Include(m => m.Bookings).ThenInclude(b => b.Court).ThenInclude(c => c.Venue)
            .Include(m => m.SlotAbsences).ThenInclude(sa => sa.UnavailablePlayer).ThenInclude(p => p.User)
            .Include(m => m.SlotAbsences).ThenInclude(sa => sa.ReplacementRequests).ThenInclude(rr => rr.Player).ThenInclude(p => p.User)
            .Include(m => m.SlotAbsences).ThenInclude(sa => sa.BookingCheckInGroup);
    }

    private static IEnumerable<MatchParticipant> ApprovedParticipants(Match match)
    {
        return match.MatchParticipants.Where(IsApproved);
    }

    private static bool IsApproved(MatchParticipant participant) =>
        participant.Status is "Approved" or "Accepted";

    private async Task<OpenMatchDetailResponse?> LoadOpenMatchResponseAsync(int matchId, int? currentPlayerId, CancellationToken cancellationToken)
    {
        var match = await MatchInvitationQuery().SingleOrDefaultAsync(m => m.MatchId == matchId, cancellationToken);
        if (match is null) return null;

        var baseSummary = MapMatchResponse(match, currentPlayerId);

        var conversation = await _matchRepository.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.MatchId == matchId && c.ConversationType == "LobbyChat", cancellationToken);

        var activeBookings = match.Bookings
            .Where(booking => IsActiveBookingStatus(booking.Status, booking.HoldExpiresAt, DateTime.UtcNow))
            .OrderByDescending(booking => booking.CreatedAt)
            .ToList();
        var firstBooking = activeBookings.FirstOrDefault() ?? match.Bookings.OrderByDescending(b => b.CreatedAt).FirstOrDefault();

        var approvedCount = Math.Max(1, match.MatchParticipants.Count(p => IsApprovedOrAccepted(p.Status)));
        var totalBookingAmount = firstBooking?.TotalAmount ?? 0m;
        var amountPerPlayer = totalBookingAmount > 0 ? Math.Round(totalBookingAmount / approvedCount, 0) : 0m;

        var myPayment = currentPlayerId.HasValue
            ? firstBooking?.Payments.FirstOrDefault(p => p.PayerId == currentPlayerId.Value)
            : null;
        var targetPayment = myPayment ?? firstBooking?.Payments.FirstOrDefault();

        var participants = match.MatchParticipants.Select(p =>
        {
            var pPayment = firstBooking?.Payments.FirstOrDefault(pay => pay.PayerId == p.PlayerId);
            return new MatchParticipantResponse
            {
                ParticipantId = p.ParticipantId,
                PlayerId = p.PlayerId,
                PlayerName = p.Player?.User?.Username ?? "Người chơi",
                AvatarUrl = p.Player?.User?.ProfileImageUrl,
                SkillLevel = p.Player?.SkillLevel ?? 0,
                Status = p.Status,
                IsHost = p.IsHost,
                RequestedAt = p.RequestedAt,
                RespondedAt = p.RespondedAt,
                CheckInStatus = "Pending",
                PaymentId = pPayment?.PaymentId,
                PaymentAmount = pPayment?.Amount ?? (IsApprovedOrAccepted(p.Status) ? amountPerPlayer : 0m),
                PaymentStatus = pPayment?.Status ?? "Pending",
                QrImageUrl = pPayment?.QrImageUrl,
                TransferContent = pPayment?.TransferContent,
                PaymentRejectionReason = pPayment?.RejectionReason
            };
        }).ToList();

        var preferredVenueIds = !string.IsNullOrEmpty(match.SharedVenues)
            ? match.SharedVenues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => int.TryParse(id, out var v) ? v : 0)
                .Where(v => v > 0)
                .ToList()
            : new List<int>();

        var preferredVenues = preferredVenueIds.Count > 0
            ? await _matchRepository.Venues
                .AsNoTracking()
                .Where(v => preferredVenueIds.Contains(v.VenueId))
                .Select(v => new MatchPreferredVenueResponse
                {
                    VenueId = v.VenueId,
                    VenueName = v.VenueName,
                    Address = v.Address,
                    Latitude = v.Latitude,
                    Longitude = v.Longitude
                })
                .ToListAsync(cancellationToken)
            : new List<MatchPreferredVenueResponse>();

        var isApprovedParticipant = currentPlayerId.HasValue
            && match.MatchParticipants.Any(participant => participant.PlayerId == currentPlayerId.Value
                && IsApprovedOrAccepted(participant.Status));
        var bookingCheckIns = await BuildVisibleBookingRoundsAsync(
            match,
            currentPlayerId,
            isApprovedParticipant,
            VietnamTime.Now,
            cancellationToken);

        return new OpenMatchDetailResponse
        {
            MatchId = match.MatchId,
            BookingId = firstBooking?.BookingId,
            HostPlayerId = match.HostPlayerId ?? 0,
            HostName = baseSummary.HostName,
            HostAvatarUrl = baseSummary.HostAvatarUrl,
            MatchType = match.MatchType,
            MatchSkillLevel = match.MatchSkillLevel,
            MinSkillLevel = match.MinSkillLevel,
            MaxSkillLevel = match.MaxSkillLevel,
            RequiredPlayerCount = match.RequiredPlayerCount,
            NeededPlayerCount = match.RequiredPlayerCount,
            AcceptedPlayerCount = baseSummary.AcceptedPlayerCount,
            Status = match.Status,
            Title = match.Title ?? string.Empty,
            Note = match.Note,
            Province = match.Province ?? string.Empty,
            Ward = match.Ward ?? string.Empty,
            SearchRadiusKm = match.SearchRadiusKm,
            SearchLatitude = match.SearchLatitude,
            SearchLongitude = match.SearchLongitude,
            AvailableDateFrom = match.AvailableDateFrom.GetValueOrDefault(),
            AvailableDateTo = match.AvailableDateTo.GetValueOrDefault(),
            PreferredTimeStart = match.PreferredTimeStart?.ToString("HH:mm") ?? string.Empty,
            PreferredTimeEnd = match.PreferredTimeEnd?.ToString("HH:mm") ?? string.Empty,
            AvailabilitySlots = baseSummary.AvailabilitySlots,
            PreferredVenues = preferredVenues,
            VenueId = baseSummary.VenueId,
            VenueName = baseSummary.VenueName,
            Address = baseSummary.Address,
            CourtId = baseSummary.CourtId,
            CourtNumber = baseSummary.CourtNumber,
            StartTime = baseSummary.StartTime,
            EndTime = baseSummary.EndTime,
            TotalBookingAmount = totalBookingAmount,
            AmountPerPlayer = amountPerPlayer,
            PaymentDeadline = firstBooking?.HoldExpiresAt,
            PaymentHoldRemainingSeconds = firstBooking?.HoldExpiresAt is not null
                ? (int)Math.Max(0, (firstBooking.HoldExpiresAt.Value - DateTime.UtcNow).TotalSeconds)
                : null,
            MyPaymentId = targetPayment?.PaymentId,
            MyPaymentStatus = targetPayment?.Status ?? "Pending",
            MyQrImageUrl = targetPayment?.QrImageUrl,
            MyTransferContent = targetPayment?.TransferContent,
            MyPaymentRejectionReason = targetPayment?.RejectionReason,
            IsHost = baseSummary.IsHost,
            MyParticipantStatus = baseSummary.MyParticipantStatus,
            ConversationId = conversation?.ConversationId,
            Participants = participants,
            BookingCheckIns = bookingCheckIns,
            MyPlayerId = currentPlayerId
        };
    }

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

    private static string BuildVietQrUrl(OwnerBankAccount account, decimal amount, string content)
    {
        var query = $"amount={Math.Round(amount):0}&addInfo={Uri.EscapeDataString(content)}&accountName={Uri.EscapeDataString(account.AccountHolderName)}";
        return $"https://img.vietqr.io/image/{Uri.EscapeDataString(account.BankCode)}-{Uri.EscapeDataString(account.AccountNumber)}-compact2.png?{query}";
    }
}
