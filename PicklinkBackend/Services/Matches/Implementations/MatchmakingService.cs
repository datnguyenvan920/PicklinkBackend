using System.Data;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches.Implementations;

public class MatchmakingService
{
    private readonly IMatchRepository _matchRepository;
    private readonly IFirebaseService? _firebaseService;
    private readonly NotificationService _notifications;
    private readonly MatchQueueSynchronizationService _matchQueueSync;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private int? _currentUserId;

    public MatchmakingService(
        IMatchRepository matchRepository,
        NotificationService notifications,
        MatchQueueSynchronizationService matchQueueSync,
        MatchRealtimeNotifier matchRealtime,
        IFirebaseService? firebaseService = null)
    {
        _matchRepository = matchRepository;
        _notifications = notifications;
        _matchQueueSync = matchQueueSync;
        _matchRealtime = matchRealtime;
        _firebaseService = firebaseService;
    }

    private async Task SyncQueueToFirebaseAsync(MatchmakingQueue queueItem, CancellationToken cancellationToken = default)
    {
        if (queueItem == null || _firebaseService == null || !_firebaseService.IsConfigured) return;

        var realtimeData = new
        {
            MatchmakingQueueId = queueItem.MatchmakingQueueId,
            MatchId = queueItem.MatchId,
            Title = queueItem.Title,
            PlayerCount = queueItem.PlayerCount,
            MatchType = queueItem.MatchType,
            SkillLevel = queueItem.SkillLevel,
            MinSkillLevel = queueItem.MinSkillLevel,
            MaxSkillLevel = queueItem.MaxSkillLevel,
            SearchLatitude = queueItem.SearchLatitude,
            SearchLongitude = queueItem.SearchLongitude,
            SearchRadiusKm = queueItem.SearchRadiusKm,
            IsActive = queueItem.IsActive,
            IsPublic = queueItem.IsPublic,
            Province = queueItem.Province,
            Ward = queueItem.Ward,
            SharedVenues = queueItem.SharedVenues,
            ReplayType = queueItem.ReplayType,
            ReplayWeekdays = queueItem.ReplayWeekdays,
            UpdatedAt = queueItem.UpdatedAt.ToString("o"),
            CreatedAt = queueItem.CreatedAt.ToString("o")
        };

        await _firebaseService.SyncQueueAsync(queueItem.MatchmakingQueueId, realtimeData, cancellationToken);
    }

    private async Task RemoveQueueFromFirebaseAsync(int queueId, CancellationToken cancellationToken = default)
    {
        if (_firebaseService == null || !_firebaseService.IsConfigured) return;
        await _firebaseService.RemoveQueueAsync(queueId, cancellationToken);
    }

    public void SetCurrentUserId(int? userId) => _currentUserId = userId;

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

    private static string? ValidateJoinSoloQueueRequest(JoinSoloQueueRequest request)
    {
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true);

        if (request.QueueSlots is not null)
        {
            if (request.QueueSlots.Count == 0)
            {
                return "Vui lòng đăng ký ít nhất một khung giờ chơi.";
            }

            foreach (var slot in request.QueueSlots)
            {
                Validator.TryValidateObject(slot, new ValidationContext(slot), validationResults, validateAllProperties: true);

                if (TimeOnly.TryParseExact(slot.TimeStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                    && TimeOnly.TryParseExact(slot.TimeEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                {
                    var startMin = start.Hour * 60 + start.Minute;
                    var endMin = (end == TimeOnly.MinValue && start > TimeOnly.MinValue) ? 24 * 60 : end.Hour * 60 + end.Minute;

                    if (startMin >= endMin)
                    {
                        return $"Khung giờ {slot.TimeStart} - {slot.TimeEnd} không hợp lệ: giờ kết thúc phải lớn hơn giờ bắt đầu.";
                    }
                }
            }
        }

        return validationResults.FirstOrDefault()?.ErrorMessage;
    }

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

    private static bool IsApproved(MatchmakingQueuePlayer queuePlayer) =>
        queuePlayer.IsHost || string.Equals(queuePlayer.Status, "Approved", StringComparison.OrdinalIgnoreCase);

    public async Task<ServiceResult<QueueStatusResponse>> GetQueueStatus(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _matchRepository.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return Ok(new QueueStatusResponse { InQueue = false });

        return await GetQueueStatusForPlayer(player.PlayerId, queueId: null, cancellationToken);
    }

    private async Task<ServiceResult<QueueStatusResponse>> GetQueueStatusForPlayer(
        int playerId,
        int? queueId,
        CancellationToken cancellationToken)
    {
        IQueryable<MatchmakingQueue> query = _matchRepository.MatchmakingQueues
            .Include(q => q.QueueSlots)
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .Include(q => q.Conversations)
            .Where(q => q.QueuePlayers.Any(qp => qp.PlayerId == playerId && qp.Status == "Approved"));

        query = queueId.HasValue
            ? query.Where(q => q.MatchmakingQueueId == queueId.Value)
            : query.OrderByDescending(q => q.IsActive).ThenByDescending(q => q.UpdatedAt);

        var queueItem = await query.FirstOrDefaultAsync(cancellationToken);

        if (queueItem is null)
            return Ok(new QueueStatusResponse { InQueue = false });

        var chat = queueItem.Conversations.FirstOrDefault(c => c.ConversationType == "QueueLobbyChat");

        var response = new QueueStatusResponse
        {
            InQueue = true,
            MatchmakingQueueId = queueItem.MatchmakingQueueId,
            MatchId = queueItem.MatchId,
            Title = queueItem.Title,
            PlayerCount = queueItem.PlayerCount,
            MatchType = queueItem.MatchType,
            SkillLevel = queueItem.SkillLevel,
            MinSkillLevel = queueItem.MinSkillLevel,
            MaxSkillLevel = queueItem.MaxSkillLevel,
            SearchLatitude = queueItem.SearchLatitude,
            SearchLongitude = queueItem.SearchLongitude,
            SearchRadiusKm = queueItem.SearchRadiusKm,
            IsActive = queueItem.IsActive,
            ReplayType = queueItem.ReplayType,
            ReplayWeekdays = queueItem.ReplayWeekdays,
            ConversationId = chat?.ConversationId,
            IsPublic = queueItem.IsPublic,
            Province = queueItem.Province,
            Ward = queueItem.Ward,
            SharedVenues = queueItem.SharedVenues,
            UpdatedAt = EnsureUtcKind(queueItem.UpdatedAt),
            CreatedAt = EnsureUtcKind(queueItem.CreatedAt),
            QueueSlots = queueItem.QueueSlots.Select(s => new QueueSlotResponse
            {
                DayOfWeek = s.DayOfWeek,
                SpecificDate = s.SpecificDate,
                DayOfMonth = s.DayOfMonth,
                TimeStart = s.TimeStart.ToString("HH:mm"),
                TimeEnd = s.TimeEnd.ToString("HH:mm")
            }).ToList(),
            QueuePlayers = queueItem.QueuePlayers.Select(qp => new QueuePlayerResponse
            {
                PlayerId = qp.PlayerId,
                PlayerName = qp.Player.User.Username,
                AvatarUrl = qp.Player.User.ProfileImageUrl,
                IsHost = qp.IsHost,
                IsCurrentPlayer = qp.PlayerId == playerId,
                Status = qp.Status
            }).ToList()
        };

        return Ok(response);
    }

    public async Task<ServiceResult<QueueStatusResponse>> JoinSoloQueue(JoinSoloQueueRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var validationError = ValidateJoinSoloQueueRequest(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var player = await _matchRepository.Players.Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var playerCount = request.PlayerCount ?? (request.MatchType == "1vs1" ? 2 : 4);
        var minSkillLevel = request.MinSkillLevel ?? 1;
        var maxSkillLevel = request.MaxSkillLevel ?? 5;

        var queueItem = new MatchmakingQueue
        {
            Title = request.Title?.Trim() ?? string.Empty,
            PlayerCount = playerCount,
            MatchType = request.MatchType,
            SkillLevel = (int)Math.Round(player.SkillLevel),
            SearchLatitude = request.SearchLatitude,
            MinSkillLevel = minSkillLevel,
            MaxSkillLevel = maxSkillLevel,
            SearchLongitude = request.SearchLongitude,
            SearchRadiusKm = request.SearchRadiusKm,
            IsActive = request.IsActive,
            ReplayType = request.ReplayType,
            ReplayWeekdays = request.ReplayWeekdays,
            IsPublic = request.IsPublic,
            Province = request.Province,
            Ward = request.Ward,
            SharedVenues = request.SharedVenues,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        queueItem.QueuePlayers.Add(new MatchmakingQueuePlayer
        {
            PlayerId = player.PlayerId,
            IsHost = true
        });

        foreach (var slotReq in request.QueueSlots)
        {
            var start = TimeOnly.ParseExact(slotReq.TimeStart, "HH:mm", CultureInfo.InvariantCulture);
            var end = TimeOnly.ParseExact(slotReq.TimeEnd, "HH:mm", CultureInfo.InvariantCulture);

            queueItem.QueueSlots.Add(new MatchmakingQueueSlot
            {
                DayOfWeek = slotReq.DayOfWeek,
                SpecificDate = slotReq.SpecificDate,
                DayOfMonth = slotReq.DayOfMonth,
                TimeStart = start,
                TimeEnd = end
            });
        }

        await _matchRepository.AddQueueAsync(queueItem, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        if (queueItem.IsPublic)
        {
            await _matchQueueSync.CreateManualMatchForQueueAsync(queueItem, cancellationToken);
            await _matchRepository.SaveChangesAsync(cancellationToken);
        }

        var conversation = new Conversation
        {
            MatchmakingQueueId = queueItem.MatchmakingQueueId,
            ConversationType = "QueueLobbyChat",
            ConversationName = $"Hàng chờ - {player.User.Username}",
            CreatedAt = DateTime.UtcNow
        };
        await _matchRepository.AddConversationAsync(conversation, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        await _matchRepository.AddConversationParticipantAsync(new ConversationParticipant
        {
            ConversationId = conversation.ConversationId,
            UserId = player.UserId,
            JoinedAt = DateTime.UtcNow
        }, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        await SyncQueueToFirebaseAsync(queueItem, cancellationToken);
        if (queueItem.MatchId.HasValue) _matchRealtime.Publish(queueItem.MatchId.Value, "MatchCreated");

        return await GetQueueStatusForPlayer(player.PlayerId, queueItem.MatchmakingQueueId, cancellationToken);
    }

    public async Task<ServiceResult<QueueStatusResponse>> JoinLobbyQueue(int matchId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _matchRepository.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        var match = await _matchRepository.Matches
            .Include(m => m.AvailabilitySlots)
            .Include(m => m.MatchParticipants).ThenInclude(mp => mp.Player).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(m => m.MatchId == matchId, cancellationToken);

        if (match is null)
            return NotFound(new { message = "Không tìm thấy phòng ghép trận." });

        if (match.HostPlayerId != player.PlayerId)
            return Forbidden(new { message = "Chỉ chủ phòng mới được đưa phòng vào hàng chờ." });

        if (match.Status != "Recruiting")
            return BadRequest(new { message = "Phòng ghép trận không ở trạng thái tuyển người." });

        var targetDaysOfWeek = new List<DayOfWeek>();
        var isWeekly = string.Equals(match.ReplayType, "Weekly", StringComparison.OrdinalIgnoreCase);
        if (isWeekly && !string.IsNullOrWhiteSpace(match.ReplayWeekdays))
        {
            var weekdayStrings = match.ReplayWeekdays.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var dayStr in weekdayStrings)
            {
                if (Enum.TryParse<DayOfWeek>(dayStr.Trim(), true, out var dow))
                {
                    targetDaysOfWeek.Add(dow);
                }
            }
        }

        var queueItem = new MatchmakingQueue
        {
            Title = match.Title ?? $"Ghép trận {match.MatchType}",
            MatchId = match.MatchId,
            PlayerCount = match.RequiredPlayerCount,
            MatchType = match.MatchType,
            SkillLevel = match.MinSkillLevel,
            SearchLatitude = match.SearchLatitude,
            SearchLongitude = match.SearchLongitude,
            MinSkillLevel = match.MinSkillLevel,
            MaxSkillLevel = match.MaxSkillLevel,
            SearchRadiusKm = match.SearchRadiusKm,
            IsActive = true,
            ReplayType = match.ReplayType,
            ReplayWeekdays = match.ReplayWeekdays,
            IsPublic = false,
            Province = match.Province,
            Ward = match.Ward,
            SharedVenues = match.SharedVenues,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var participants = match.MatchParticipants
            .Where(mp => mp.Status is "Approved" or "Accepted")
            .ToList();
        foreach (var p in participants)
        {
            queueItem.QueuePlayers.Add(new MatchmakingQueuePlayer
            {
                PlayerId = p.PlayerId,
                IsHost = p.PlayerId == match.HostPlayerId
            });
        }

        if (match.AvailabilitySlots.Count > 0)
        {
            foreach (var slot in match.AvailabilitySlots)
            {
                if (isWeekly && targetDaysOfWeek.Count > 0)
                {
                    foreach (var dow in targetDaysOfWeek)
                    {
                        queueItem.QueueSlots.Add(new MatchmakingQueueSlot
                        {
                            DayOfWeek = dow,
                            TimeStart = slot.TimeStart,
                            TimeEnd = slot.TimeEnd
                        });
                    }
                }
                else if (string.Equals(match.ReplayType, "Daily", StringComparison.OrdinalIgnoreCase))
                {
                    queueItem.QueueSlots.Add(new MatchmakingQueueSlot
                    {
                        TimeStart = slot.TimeStart,
                        TimeEnd = slot.TimeEnd
                    });
                }
                else
                {
                    var fromDate = match.AvailableDateFrom ?? DateOnly.FromDateTime(VietnamTime.Now);
                    var toDate = match.AvailableDateTo ?? fromDate;

                    if (toDate.DayNumber - fromDate.DayNumber > 30)
                    {
                        toDate = fromDate.AddDays(30);
                    }

                    for (var current = fromDate; current <= toDate; current = current.AddDays(1))
                    {
                        queueItem.QueueSlots.Add(new MatchmakingQueueSlot
                        {
                            SpecificDate = current,
                            TimeStart = slot.TimeStart,
                            TimeEnd = slot.TimeEnd
                        });
                    }
                }
            }
        }
        else
        {
            var timeStart = match.PreferredTimeStart ?? new TimeOnly(8, 0);
            var timeEnd = match.PreferredTimeEnd ?? new TimeOnly(22, 0);
            var fromDate = match.AvailableDateFrom ?? DateOnly.FromDateTime(VietnamTime.Now);
            var toDate = match.AvailableDateTo ?? fromDate;
            if (toDate.DayNumber - fromDate.DayNumber > 30) toDate = fromDate.AddDays(30);

            for (var current = fromDate; current <= toDate; current = current.AddDays(1))
            {
                queueItem.QueueSlots.Add(new MatchmakingQueueSlot
                {
                    SpecificDate = current,
                    TimeStart = timeStart,
                    TimeEnd = timeEnd
                });
            }
        }

        await _matchRepository.AddQueueAsync(queueItem, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        var conversation = new Conversation
        {
            MatchmakingQueueId = queueItem.MatchmakingQueueId,
            ConversationType = "QueueLobbyChat",
            ConversationName = $"Hàng chờ - {match.Title}",
            CreatedAt = DateTime.UtcNow
        };
        await _matchRepository.AddConversationAsync(conversation, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        foreach (var qp in queueItem.QueuePlayers)
        {
            var p = participants.First(part => part.PlayerId == qp.PlayerId);
            await _matchRepository.AddConversationParticipantAsync(new ConversationParticipant
            {
                ConversationId = conversation.ConversationId,
                UserId = p.Player.UserId,
                JoinedAt = DateTime.UtcNow
            }, cancellationToken);
        }
        await _matchRepository.SaveChangesAsync(cancellationToken);

        await SyncQueueToFirebaseAsync(queueItem, cancellationToken);

        return await GetQueueStatusForPlayer(player.PlayerId, queueItem.MatchmakingQueueId, cancellationToken);
    }

    public async Task<ServiceResult<List<QueueStatusResponse>>> GetMyQueues(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _matchRepository.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return Ok(new List<QueueStatusResponse>());

        var queues = await _matchRepository.MatchmakingQueues
            .AsNoTracking()
            .AsSplitQuery()
            .Include(q => q.QueueSlots)
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .Include(q => q.Conversations)
            .Where(q => q.QueuePlayers.Any(qp => qp.PlayerId == player.PlayerId && qp.Status == "Approved"))
            .ToListAsync(cancellationToken);

        var list = queues.Select(queueItem => {
            var chat = queueItem.Conversations.FirstOrDefault(c => c.ConversationType == "QueueLobbyChat");
            return new QueueStatusResponse
            {
                InQueue = true,
                MatchmakingQueueId = queueItem.MatchmakingQueueId,
                MatchId = queueItem.MatchId,
                Title = queueItem.Title,
                PlayerCount = queueItem.PlayerCount,
                MatchType = queueItem.MatchType,
                SkillLevel = queueItem.SkillLevel,
                SearchLatitude = queueItem.SearchLatitude,
                SearchLongitude = queueItem.SearchLongitude,
                MinSkillLevel = queueItem.MinSkillLevel,
                MaxSkillLevel = queueItem.MaxSkillLevel,
                SearchRadiusKm = queueItem.SearchRadiusKm,
                IsActive = queueItem.IsActive,
                ReplayType = queueItem.ReplayType,
                ReplayWeekdays = queueItem.ReplayWeekdays,
                ConversationId = chat?.ConversationId,
                IsPublic = queueItem.IsPublic,
                Province = queueItem.Province,
                Ward = queueItem.Ward,
                SharedVenues = queueItem.SharedVenues,
                UpdatedAt = EnsureUtcKind(queueItem.UpdatedAt),
                CreatedAt = EnsureUtcKind(queueItem.CreatedAt),
                QueueSlots = queueItem.QueueSlots.Select(s => new QueueSlotResponse
                {
                    DayOfWeek = s.DayOfWeek,
                    SpecificDate = s.SpecificDate,
                    DayOfMonth = s.DayOfMonth,
                    TimeStart = s.TimeStart.ToString("HH:mm"),
                    TimeEnd = s.TimeEnd.ToString("HH:mm")
                }).ToList(),
                QueuePlayers = queueItem.QueuePlayers.Select(qp => new QueuePlayerResponse
                {
                    PlayerId = qp.PlayerId,
                    PlayerName = qp.Player.User.Username,
                    AvatarUrl = qp.Player.User.ProfileImageUrl,
                    IsHost = qp.IsHost,
                    IsCurrentPlayer = qp.PlayerId == player.PlayerId,
                    Status = qp.Status
                }).ToList()
            };
        }).ToList();

        return Ok(list);
    }

    public async Task<ServiceResult> CancelQueue(int? queueId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _matchRepository.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return NotFound(new { message = "Hồ sơ người chơi không tồn tại." });

        var query = _matchRepository.MatchmakingQueues.Include(q => q.QueuePlayers).AsQueryable();
        if (queueId.HasValue)
            query = query.Where(q => q.MatchmakingQueueId == queueId.Value);
        else
            query = query.Where(q => q.QueuePlayers.Any(qp => qp.PlayerId == player.PlayerId));

        var queueItem = await query.FirstOrDefaultAsync(cancellationToken);

        if (queueItem is not null)
        {
            var qpEntry = queueItem.QueuePlayers.FirstOrDefault(qp => qp.PlayerId == player.PlayerId);
            if (qpEntry is not null)
            {
                if (qpEntry.IsHost)
                {
                    if (queueItem.IsPublic && queueItem.MatchId.HasValue)
                    {
                        var linkedMatch = await _matchRepository.Matches
                            .SingleOrDefaultAsync(match => match.MatchId == queueItem.MatchId.Value, cancellationToken);
                        if (linkedMatch is not null && linkedMatch.Status is "Recruiting" or "ReadyToBook")
                            linkedMatch.Status = "Cancelled";
                    }
                    await DeleteMatchmakingQueues(new List<MatchmakingQueue> { queueItem }, cancellationToken);
                }
                else
                {
                    qpEntry.Status = "Left";
                    await _matchQueueSync.SyncQueuePlayerToMatchAsync(queueItem, qpEntry, cancellationToken);
                    await _matchRepository.RemoveQueuePlayerAsync(qpEntry, cancellationToken);

                    var conversation = await _matchRepository.Conversations
                        .FirstOrDefaultAsync(c => c.MatchmakingQueueId == queueItem.MatchmakingQueueId && c.ConversationType == "QueueLobbyChat", cancellationToken);
                    if (conversation is not null)
                    {
                        var convParticipant = await _matchRepository.ConversationParticipants
                            .FirstOrDefaultAsync(cp => cp.ConversationId == conversation.ConversationId && cp.UserId == player.UserId, cancellationToken);
                        if (convParticipant is not null)
                        {
                            await _matchRepository.RemoveConversationParticipantAsync(convParticipant, cancellationToken);
                        }
                    }
                }

                await _matchRepository.SaveChangesAsync(cancellationToken);
                if (!qpEntry.IsHost) await SyncQueueToFirebaseAsync(queueItem, cancellationToken);
                if (queueItem.MatchId.HasValue)
                    _matchRealtime.Publish(queueItem.MatchId.Value, qpEntry.IsHost ? "Cancelled" : "ParticipantWithdrawn");
            }
        }

        return Ok(new { message = "Đã rời hàng chờ ghép trận." });
    }

    public async Task<ServiceResult<QueueStatusResponse>> ResumeQueue(int? queueId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _matchRepository.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return NotFound(new { message = "Hồ sơ người chơi không tồn tại." });

        var query = _matchRepository.MatchmakingQueues
            .Include(q => q.QueueSlots)
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .Include(q => q.Conversations)
            .AsQueryable();

        if (queueId.HasValue)
            query = query.Where(q => q.MatchmakingQueueId == queueId.Value);
        else
            query = query.Where(q => !q.IsActive && q.QueuePlayers.Any(qp => qp.PlayerId == player.PlayerId));

        var queueItem = await query.FirstOrDefaultAsync(cancellationToken);

        if (queueItem is null)
            return BadRequest(new { message = "Không tìm thấy hàng chờ để kích hoạt lại." });
        if (!queueItem.QueuePlayers.Any(qp => qp.PlayerId == player.PlayerId && IsApproved(qp)))
            return Forbidden(new { message = "Bạn không thuộc hàng chờ này." });
        if (queueItem.MatchId.HasValue)
            return BadRequest(new { message = "Hàng chờ đã tạo phòng và không thể kích hoạt lại." });

        queueItem.IsActive = true;
        queueItem.UpdatedAt = DateTime.UtcNow;
        await _matchRepository.SaveChangesAsync(cancellationToken);

        await SyncQueueToFirebaseAsync(queueItem, cancellationToken);

        var chat = queueItem.Conversations.FirstOrDefault(c => c.ConversationType == "QueueLobbyChat");
        var response = new QueueStatusResponse
        {
            InQueue = true,
            MatchmakingQueueId = queueItem.MatchmakingQueueId,
            MatchId = queueItem.MatchId,
            Title = queueItem.Title,
            PlayerCount = queueItem.PlayerCount,
            MatchType = queueItem.MatchType,
            SkillLevel = queueItem.SkillLevel,
            SearchLatitude = queueItem.SearchLatitude,
            SearchLongitude = queueItem.SearchLongitude,
            MinSkillLevel = queueItem.MinSkillLevel,
            MaxSkillLevel = queueItem.MaxSkillLevel,
            SearchRadiusKm = queueItem.SearchRadiusKm,
            IsActive = queueItem.IsActive,
            ReplayType = queueItem.ReplayType,
            ReplayWeekdays = queueItem.ReplayWeekdays,
            ConversationId = chat?.ConversationId,
            IsPublic = queueItem.IsPublic,
            Province = queueItem.Province,
            Ward = queueItem.Ward,
            SharedVenues = queueItem.SharedVenues,
            UpdatedAt = EnsureUtcKind(queueItem.UpdatedAt),
            CreatedAt = EnsureUtcKind(queueItem.CreatedAt),
            QueueSlots = queueItem.QueueSlots.Select(s => new QueueSlotResponse
            {
                DayOfWeek = s.DayOfWeek,
                SpecificDate = s.SpecificDate,
                DayOfMonth = s.DayOfMonth,
                TimeStart = s.TimeStart.ToString("HH:mm"),
                TimeEnd = s.TimeEnd.ToString("HH:mm")
            }).ToList(),
            QueuePlayers = queueItem.QueuePlayers.Select(qp => new QueuePlayerResponse
            {
                PlayerId = qp.PlayerId,
                PlayerName = qp.Player.User.Username,
                AvatarUrl = qp.Player.User.ProfileImageUrl,
                IsHost = qp.IsHost,
                Status = qp.Status
            }).ToList()
        };

        return Ok(response);
    }

    public async Task<ServiceResult<IReadOnlyList<QueueStatusResponse>>> GetPublicQueues(CancellationToken cancellationToken)
    {
        int? currentPlayerId = null;
        if (TryGetCurrentUserId(out var currentUserId))
            currentPlayerId = await _matchRepository.Players.Where(p => p.UserId == currentUserId)
                .Select(p => (int?)p.PlayerId).FirstOrDefaultAsync(cancellationToken);

        var queues = await _matchRepository.MatchmakingQueues
            .AsNoTracking()
            .AsSplitQuery()
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .Include(q => q.QueueSlots)
            .Where(q =>
                q.IsActive &&
                q.IsPublic &&
                q.QueuePlayers.Count(qp => qp.Status == "Approved") < q.PlayerCount)
            .OrderByDescending(q => q.UpdatedAt)
            .ToListAsync(cancellationToken);

        var responses = queues.Select(q => new QueueStatusResponse
        {
            InQueue = true,
            MatchmakingQueueId = q.MatchmakingQueueId,
            MatchId = q.MatchId,
            Title = q.Title,
            PlayerCount = q.PlayerCount,
            MatchType = q.MatchType,
            SkillLevel = q.SkillLevel,
            SearchRadiusKm = q.SearchRadiusKm,
            SearchLatitude = null,
            MinSkillLevel = q.MinSkillLevel,
            MaxSkillLevel = q.MaxSkillLevel,
            SearchLongitude = null,
            IsActive = q.IsActive,
            ReplayType = q.ReplayType,
            ConversationId = null,
            IsPublic = q.IsPublic,
            Province = q.Province,
            Ward = q.Ward,
            SharedVenues = q.SharedVenues,
            UpdatedAt = EnsureUtcKind(q.UpdatedAt),
            CreatedAt = EnsureUtcKind(q.CreatedAt),
            QueuePlayers = q.QueuePlayers
                .Where(qp => IsApproved(qp) || qp.PlayerId == currentPlayerId).Select(qp => new QueuePlayerResponse
            {
                PlayerId = qp.PlayerId,
                PlayerName = qp.Player.User.Username,
                AvatarUrl = qp.Player.User.ProfileImageUrl,
                IsHost = qp.IsHost,
                IsCurrentPlayer = qp.PlayerId == currentPlayerId,
                Status = qp.Status
            }).ToList(),
            QueueSlots = q.QueueSlots.Select(qs => new QueueSlotResponse
            {
                DayOfWeek = qs.DayOfWeek,
                SpecificDate = qs.SpecificDate,
                DayOfMonth = qs.DayOfMonth,
                TimeStart = qs.TimeStart.ToString(@"hh\:mm"),
                TimeEnd = qs.TimeEnd.ToString(@"hh\:mm")
            }).ToList()
        }).ToList();

        return Ok(responses);
    }

    public async Task<ServiceResult<QueueStatusResponse>> GetQueueById(int queueId, CancellationToken cancellationToken)
    {
        int? currentPlayerId = null;
        if (TryGetCurrentUserId(out var currentUserId))
            currentPlayerId = await _matchRepository.Players.Where(p => p.UserId == currentUserId)
                .Select(p => (int?)p.PlayerId).FirstOrDefaultAsync(cancellationToken);

        var q = await _matchRepository.MatchmakingQueues
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .Include(q => q.QueueSlots)
            .FirstOrDefaultAsync(q => q.MatchmakingQueueId == queueId, cancellationToken);

        if (q is null)
            return NotFound(new { message = "Không tìm thấy lời mời ghép trận này." });

        var response = new QueueStatusResponse
        {
            InQueue = currentPlayerId.HasValue && q.QueuePlayers.Any(qp => qp.PlayerId == currentPlayerId.Value && IsApproved(qp)),
            MatchmakingQueueId = q.MatchmakingQueueId,
            MatchId = q.MatchId,
            Title = q.Title,
            PlayerCount = q.PlayerCount,
            MatchType = q.MatchType,
            SkillLevel = q.SkillLevel,
            SearchRadiusKm = q.SearchRadiusKm,
            SearchLatitude = q.SearchLatitude,
            MinSkillLevel = q.MinSkillLevel,
            MaxSkillLevel = q.MaxSkillLevel,
            SearchLongitude = q.SearchLongitude,
            IsActive = q.IsActive,
            ReplayType = q.ReplayType,
            ConversationId = null,
            IsPublic = q.IsPublic,
            Province = q.Province,
            Ward = q.Ward,
            SharedVenues = q.SharedVenues,
            UpdatedAt = EnsureUtcKind(q.UpdatedAt),
            CreatedAt = EnsureUtcKind(q.CreatedAt),
            QueuePlayers = q.QueuePlayers.Select(qp => new QueuePlayerResponse
            {
                PlayerId = qp.PlayerId,
                PlayerName = qp.Player?.User?.Username ?? "Unknown",
                AvatarUrl = qp.Player?.User?.ProfileImageUrl,
                IsHost = qp.IsHost,
                IsCurrentPlayer = currentPlayerId.HasValue && qp.PlayerId == currentPlayerId.Value,
                Status = qp.Status
            }).ToList(),
            QueueSlots = q.QueueSlots.Select(qs => new QueueSlotResponse
            {
                DayOfWeek = qs.DayOfWeek,
                SpecificDate = qs.SpecificDate,
                DayOfMonth = qs.DayOfMonth,
                TimeStart = qs.TimeStart.ToString(@"hh\:mm"),
                TimeEnd = qs.TimeEnd.ToString(@"hh\:mm")
            }).ToList()
        };

        return Ok(response);
    }

    public async Task<ServiceResult<QueueStatusResponse>> AcceptQueueInvite(int queueId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _matchRepository.Players
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"matchmaking-queue:{queueId}", cancellationToken))
            return new ServiceResult<QueueStatusResponse>(ServiceResultStatus.Conflict, Error: new { message = "Hàng chờ đang được xử lý." });

        var queue = await _matchRepository.MatchmakingQueues
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .Include(q => q.QueueSlots)
            .FirstOrDefaultAsync(q => q.MatchmakingQueueId == queueId, cancellationToken);

        if (queue is null)
            return NotFound(new { message = "Không tìm thấy hàng chờ ghép trận này." });

        if (!queue.IsActive)
            return BadRequest(new { message = "Hàng chờ này hiện không còn hoạt động hoặc đã kết thúc." });

        var approvedCount = queue.QueuePlayers.Count(IsApproved);
        if (approvedCount >= queue.PlayerCount)
            return BadRequest(new { message = "Rất tiếc, hàng chờ này đã đủ người chơi trước khi bạn chấp nhận." });

        if (queue.QueuePlayers.Any(qp => qp.PlayerId == player.PlayerId && IsApproved(qp)))
            return BadRequest(new { message = "Bạn đã ở trong hàng chờ này rồi." });

        if (player.SkillLevel < queue.MinSkillLevel || player.SkillLevel > queue.MaxSkillLevel)
            return BadRequest(new { message = $"Trình độ của bạn không nằm trong khoảng Level {queue.MinSkillLevel}-{queue.MaxSkillLevel}." });

        var existingQP = queue.QueuePlayers.FirstOrDefault(qp => qp.PlayerId == player.PlayerId);
        if (queue.IsPublic && existingQP?.Status != "Invited")
            return BadRequest(new { message = "Bạn không có lời mời đang chờ cho hàng chờ này." });
        MatchmakingQueuePlayer acceptedQueuePlayer;
        if (existingQP != null)
        {
            existingQP.Status = "Approved";
            acceptedQueuePlayer = existingQP;
        }
        else
        {
            var newQP = new MatchmakingQueuePlayer
            {
                MatchmakingQueueId = queueId,
                PlayerId = player.PlayerId,
                IsHost = false,
                Status = "Approved"
            };
            await _matchRepository.AddQueuePlayerAsync(newQP, cancellationToken);
            if (!queue.QueuePlayers.Contains(newQP)) queue.QueuePlayers.Add(newQP);
            acceptedQueuePlayer = newQP;
        }

        queue.UpdatedAt = DateTime.UtcNow;
        await _matchQueueSync.SyncQueuePlayerToMatchAsync(queue, acceptedQueuePlayer, cancellationToken);

        var hostQP = queue.QueuePlayers.FirstOrDefault(qp => qp.IsHost);
        if (hostQP != null && hostQP.Player.UserId != userId)
        {
            _notifications.Add(new NotificationInput(
                UserId: hostQP.Player.UserId,
                Type: NotificationTypes.Match,
                Title: "Bạn bè đã tham gia hàng chờ",
                Message: $"{player.User.Username} đã chấp nhận lời mời và tham gia hàng chờ #{queue.MatchmakingQueueId}.",
                Tone: NotificationTones.Success,
                LinkTo: $"/opponents/queue/{queue.MatchmakingQueueId}",
                LinkLabel: "Xem hàng chờ"));
        }

        await _matchRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _notifications.PublishPending();
        await SyncQueueToFirebaseAsync(queue, cancellationToken);
        if (queue.MatchId.HasValue) _matchRealtime.Publish(queue.MatchId.Value, "InvitationAccepted");

        return await GetQueueById(queueId, cancellationToken);
    }

    public async Task<ServiceResult<QueueStatusResponse>> JoinPublicQueue(int queueId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _matchRepository.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });
        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"matchmaking-queue:{queueId}", cancellationToken))
            return new ServiceResult<QueueStatusResponse>(ServiceResultStatus.Conflict, Error: new { message = "Hàng chờ đang được xử lý." });

        var targetQueue = await _matchRepository.MatchmakingQueues
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .Include(q => q.QueueSlots)
            .FirstOrDefaultAsync(q => q.MatchmakingQueueId == queueId, cancellationToken);

        if (targetQueue is null)
            return NotFound(new { message = "Không tìm thấy hàng chờ này." });

        if (!targetQueue.IsActive)
            return BadRequest(new { message = "Hàng chờ này đã bị tạm dừng." });

        var maxCapacity = targetQueue.PlayerCount;
        if (targetQueue.QueuePlayers.Count(IsApproved) >= maxCapacity)
            return BadRequest(new { message = "Hàng chờ này đã đầy thành viên." });

        if (targetQueue.QueuePlayers.Any(qp => qp.PlayerId == player.PlayerId))
            return BadRequest(new { message = "Bạn đã tham gia hàng chờ này rồi." });

        if (player.SkillLevel < targetQueue.MinSkillLevel || player.SkillLevel > targetQueue.MaxSkillLevel)
            return BadRequest(new { message = $"Trình độ của bạn không nằm trong khoảng Level {targetQueue.MinSkillLevel}-{targetQueue.MaxSkillLevel}." });

        var queuePlayer = new MatchmakingQueuePlayer
        {
            MatchmakingQueueId = queueId,
            PlayerId = player.PlayerId,
            IsHost = false,
            Status = "Pending"
        };
        await _matchRepository.AddQueuePlayerAsync(queuePlayer, cancellationToken);
        targetQueue.UpdatedAt = DateTime.UtcNow;
        await _matchQueueSync.SyncQueuePlayerToMatchAsync(targetQueue, queuePlayer, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await SyncQueueToFirebaseAsync(targetQueue, cancellationToken);
        if (targetQueue.MatchId.HasValue) _matchRealtime.Publish(targetQueue.MatchId.Value, "JoinRequested");

        return Ok(new QueueStatusResponse { InQueue = false, MatchmakingQueueId = queueId });
    }

    public async Task<ServiceResult<object>> CreateManualQueueRoom(int queueId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out _)) return Unauthorized();

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"matchmaking-queue:{queueId}", cancellationToken))
            return new ServiceResult<object>(ServiceResultStatus.Conflict, Error: new { message = "Hàng chờ đang được xử lý." });

        var queue = await _matchRepository.MatchmakingQueues
            .Include(item => item.QueueSlots)
            .Include(item => item.QueuePlayers).ThenInclude(item => item.Player)
            .SingleOrDefaultAsync(item => item.MatchmakingQueueId == queueId, cancellationToken);
        if (queue is null) return NotFound(new { message = "Không tìm thấy hàng chờ ghép trận." });
        if (!queue.IsPublic) return BadRequest(new { message = "Chỉ hàng chờ ghép thủ công mới có thể mở phòng." });
        if (queue.MatchId is int existingMatchId)
            return Ok(new { matchId = existingMatchId });

        var match = await _matchQueueSync.CreateManualMatchForQueueAsync(queue, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await SyncQueueToFirebaseAsync(queue, cancellationToken);
        _matchRealtime.Publish(match.MatchId, "MatchCreated");
        return Ok(new { matchId = match.MatchId });
    }

    public async Task<ServiceResult> ReviewPublicQueueRequest(int queueId, int playerId, bool approve, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"matchmaking-queue:{queueId}", cancellationToken))
            return new ServiceResult(ServiceResultStatus.Conflict, Error: new { message = "Hàng chờ đang được xử lý." });

        var targetQueue = await _matchRepository.MatchmakingQueues
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(q => q.MatchmakingQueueId == queueId, cancellationToken);

        if (targetQueue is null)
            return NotFound(new { message = "Không tìm thấy hàng chờ này." });

        var host = targetQueue.QueuePlayers.FirstOrDefault(qp => qp.IsHost && IsApproved(qp));
        if (host?.Player.UserId != userId)
            return Forbidden(new { message = "Chỉ chủ phòng mới có quyền duyệt yêu cầu." });

        var request = targetQueue.QueuePlayers.FirstOrDefault(qp => qp.PlayerId == playerId && qp.Status == "Pending");
        if (request is null)
            return NotFound(new { message = "Không tìm thấy yêu cầu đang chờ duyệt." });

        if (approve && targetQueue.QueuePlayers.Count(IsApproved) >= targetQueue.PlayerCount)
            return BadRequest(new { message = "Hàng chờ này đã đủ thành viên được duyệt." });

        request.Status = approve ? "Approved" : "Rejected";
        targetQueue.UpdatedAt = DateTime.UtcNow;
        await _matchQueueSync.SyncQueuePlayerToMatchAsync(targetQueue, request, cancellationToken);

        if (approve)
        {
            var conversation = await _matchRepository.Conversations
                .FirstOrDefaultAsync(c => c.MatchmakingQueueId == queueId && c.ConversationType == "QueueLobbyChat", cancellationToken);
            if (conversation is not null)
            {
                var isParticipant = await _matchRepository.ConversationParticipants
                    .AnyAsync(cp => cp.ConversationId == conversation.ConversationId && cp.UserId == request.Player.UserId, cancellationToken);
                if (!isParticipant)
                    await _matchRepository.AddConversationParticipantAsync(new ConversationParticipant { ConversationId = conversation.ConversationId, UserId = request.Player.UserId, JoinedAt = DateTime.UtcNow }, cancellationToken);
            }
        }

        await _matchRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await SyncQueueToFirebaseAsync(targetQueue, cancellationToken);
        if (targetQueue.MatchId.HasValue)
            _matchRealtime.Publish(targetQueue.MatchId.Value, approve ? "ParticipantApproved" : "ParticipantRejected");

        return Ok(new { message = approve ? "Đã chấp nhận yêu cầu tham gia." : "Đã từ chối yêu cầu tham gia." });
    }

    private async Task DeleteMatchmakingQueues(List<MatchmakingQueue> queues, CancellationToken cancellationToken)
    {
        if (queues == null || queues.Count == 0) return;

        var queueIds = queues.Select(q => q.MatchmakingQueueId).ToList();

        var conversations = await _matchRepository.Conversations
            .Where(c => c.MatchmakingQueueId.HasValue && queueIds.Contains(c.MatchmakingQueueId.Value))
            .ToListAsync(cancellationToken);

        if (conversations.Count > 0)
        {
            var conversationIds = conversations.Select(c => c.ConversationId).ToList();

            var participants = await _matchRepository.ConversationParticipants
                .Where(cp => conversationIds.Contains(cp.ConversationId))
                .ToListAsync(cancellationToken);
            if (participants.Count > 0)
            {
                await _matchRepository.RemoveRangeConversationParticipantsAsync(participants, cancellationToken);
            }

            var messages = await _matchRepository.Messages
                .Where(m => conversationIds.Contains(m.ConversationId))
                .ToListAsync(cancellationToken);
            if (messages.Count > 0)
            {
                await _matchRepository.RemoveRangeMessagesAsync(messages, cancellationToken);
            }

            await _matchRepository.RemoveRangeConversationsAsync(conversations, cancellationToken);
        }

        await _matchRepository.RemoveRangeQueuesAsync(queues, cancellationToken);

        foreach (var q in queues)
        {
            await RemoveQueueFromFirebaseAsync(q.MatchmakingQueueId, cancellationToken);
        }
    }

    private static DateTime? EnsureUtcKind(DateTime? dateTime)
    {
        if (!dateTime.HasValue) return null;
        return DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc);
    }

    public async Task<ServiceResult<ClearOverdueResultDto>> ClearOverdue(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var localNow = VietnamTime.FromUtc(nowUtc);
        var today = DateOnly.FromDateTime(localNow);
        var currentTime = TimeOnly.FromDateTime(localNow);

        int expiredQueuesCount = 0;
        int expiredMatchesCount = 0;
        int completedMatchesCount = 0;
        var completedMatchIds = new List<int>();

        // 1. Clear overdue MatchmakingQueues
        var activeQueues = await _matchRepository.MatchmakingQueues
            .Include(q => q.QueueSlots)
            .Where(q => q.IsActive)
            .ToListAsync(cancellationToken);

        var queuesToDeactivate = new List<MatchmakingQueue>();
        foreach (var queue in activeQueues)
        {
            var isOverdue = false;

            if (queue.QueueSlots.Count > 0 && queue.QueueSlots.Any(s => s.SpecificDate.HasValue))
            {
                var specificDateSlots = queue.QueueSlots.Where(s => s.SpecificDate.HasValue).ToList();
                if (specificDateSlots.All(s => IsSlotOverdue(s, today, currentTime)))
                {
                    isOverdue = true;
                }
            }
            else if (string.Equals(queue.ReplayType, "None", StringComparison.OrdinalIgnoreCase))
            {
                if (queue.UpdatedAt < nowUtc.AddDays(-3))
                {
                    isOverdue = true;
                }
            }

            if (isOverdue)
            {
                queue.IsActive = false;
                queue.UpdatedAt = nowUtc;
                queuesToDeactivate.Add(queue);
                expiredQueuesCount++;
            }
        }

        if (queuesToDeactivate.Count > 0)
        {
            await _matchRepository.SaveChangesAsync(cancellationToken);
            foreach (var q in queuesToDeactivate)
            {
                await RemoveQueueFromFirebaseAsync(q.MatchmakingQueueId, cancellationToken);
            }
        }

        // 2. Clear overdue Matches (Recruiting / ReadyToBook / BookingPending)
        var pendingMatches = await _matchRepository.Matches
            .Include(m => m.Bookings)
            .Where(m => m.Status == "Recruiting" || m.Status == "ReadyToBook" || m.Status == "BookingPending")
            .ToListAsync(cancellationToken);

        foreach (var match in pendingMatches)
        {
            var isOverdue = false;

            if (match.AvailableDateTo.HasValue)
            {
                if (match.AvailableDateTo.Value < today)
                {
                    isOverdue = true;
                }
                else if (match.AvailableDateTo.Value == today && match.PreferredTimeEnd.HasValue && match.PreferredTimeEnd.Value <= currentTime)
                {
                    isOverdue = true;
                }
            }
            else if (match.MatchTime.HasValue && match.MatchTime.Value < nowUtc)
            {
                isOverdue = true;
            }
            else if (match.CreatedAt < nowUtc.AddDays(-7) && !match.Bookings.Any(b => b.Status == "Confirmed"))
            {
                isOverdue = true;
            }

            if (isOverdue)
            {
                match.Status = "Expired";
                expiredMatchesCount++;

                foreach (var b in match.Bookings.Where(b => b.Status == "Holding" || b.Status == "Pending"))
                {
                    b.Status = "Expired";
                }
            }
        }

        // 3. Mark completed matches
        var bookedMatches = await _matchRepository.Matches
            .Include(m => m.Bookings).ThenInclude(booking => booking.StatusHistories)
            .Where(m => m.Status == "Booked")
            .ToListAsync(cancellationToken);

        foreach (var match in bookedMatches)
        {
            var activeBookings = match.Bookings.Where(b => b.Status is "Confirmed" or "Completed").ToList();
            if (activeBookings.Count > 0 && activeBookings.All(b => b.EndTime <= localNow))
            {
                foreach (var booking in activeBookings.Where(booking => booking.Status == "Confirmed"))
                {
                    booking.Status = "Completed";
                    booking.StatusHistories.Add(new BookingStatusHistory
                    {
                        FromStatus = "Confirmed",
                        ToStatus = "Completed",
                        Reason = "MatchCompleted",
                        ChangedAt = nowUtc
                    });
                }

                match.Status = "Completed";
                completedMatchIds.Add(match.MatchId);
                completedMatchesCount++;
            }
        }

        if (expiredMatchesCount > 0 || completedMatchesCount > 0)
        {
            await _matchRepository.SaveChangesAsync(cancellationToken);
        }

        foreach (var completedMatchId in completedMatchIds)
        {
            _matchRealtime.Publish(completedMatchId, "MatchCompleted");
        }

        // 4. Remove dead/stale queue records (Inactive + Private and not modified for 10 days)
        var tenDaysAgo = nowUtc.AddDays(-10);
        var staleQueues = await _matchRepository.MatchmakingQueues
            .Where(q => !q.IsActive && !q.IsPublic && q.UpdatedAt < tenDaysAgo)
            .ToListAsync(cancellationToken);

        int deletedDeadQueuesCount = staleQueues.Count;
        if (staleQueues.Count > 0)
        {
            await DeleteMatchmakingQueues(staleQueues, cancellationToken);
        }

        return new ServiceResult<ClearOverdueResultDto>(ServiceResultStatus.Success, new ClearOverdueResultDto
        {
            ExpiredQueuesCount = expiredQueuesCount,
            ExpiredMatchesCount = expiredMatchesCount,
            CompletedMatchesCount = completedMatchesCount,
            DeletedDeadQueuesCount = deletedDeadQueuesCount,
            ClearedAt = nowUtc
        });
    }

    private static bool IsSlotOverdue(MatchmakingQueueSlot s, DateOnly today, TimeOnly currentTime)
    {
        if (!s.SpecificDate.HasValue) return false;
        if (s.SpecificDate.Value < today) return true;
        if (s.SpecificDate.Value > today) return false;

        var endMin = (s.TimeEnd == TimeOnly.MinValue && s.TimeStart > TimeOnly.MinValue) ? 24 * 60 : s.TimeEnd.Hour * 60 + s.TimeEnd.Minute;
        var currentMin = currentTime.Hour * 60 + currentTime.Minute;
        return endMin <= currentMin;
    }

    public async Task<ServiceResult> InviteFriendToQueue(
        int queueId,
        int targetUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized();

        if (targetUserId <= 0 || targetUserId == currentUserId)
            return BadRequest(new { message = "Người dùng được mời không hợp lệ." });

        var queue = await _matchRepository.MatchmakingQueues
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(q => q.MatchmakingQueueId == queueId, cancellationToken);

        if (queue is null)
            return NotFound(new { message = "Không tìm thấy hàng chờ ghép trận." });

        var senderPlayer = queue.QueuePlayers.FirstOrDefault(qp => qp.Player.UserId == currentUserId && IsApproved(qp));
        if (senderPlayer is null)
            return Forbidden(new { message = "Bạn cần tham gia vào hàng chờ này trước để mời bạn bè." });

        var targetPlayer = await _matchRepository.Players
            .Include(player => player.User)
            .SingleOrDefaultAsync(player => player.UserId == targetUserId, cancellationToken);
        var targetUser = targetPlayer?.User;

        if (targetUser is null)
            return NotFound(new { message = "Không tìm thấy người chơi được mời." });

        if (queue.QueuePlayers.Any(qp => qp.Player.UserId == targetUserId && IsApproved(qp)))
            return BadRequest(new { message = $"{targetUser.Username} đã ở trong hàng chờ này." });

        var invitedQueuePlayer = queue.QueuePlayers.FirstOrDefault(qp => qp.PlayerId == targetPlayer!.PlayerId);
        if (invitedQueuePlayer is null)
        {
            invitedQueuePlayer = new MatchmakingQueuePlayer
            {
                MatchmakingQueueId = queue.MatchmakingQueueId,
                PlayerId = targetPlayer!.PlayerId,
                IsHost = false,
                Status = "Invited"
            };
            await _matchRepository.AddQueuePlayerAsync(invitedQueuePlayer, cancellationToken);
            if (!queue.QueuePlayers.Contains(invitedQueuePlayer)) queue.QueuePlayers.Add(invitedQueuePlayer);
        }
        else
        {
            invitedQueuePlayer.Status = "Invited";
        }
        await _matchQueueSync.SyncQueuePlayerToMatchAsync(queue, invitedQueuePlayer, cancellationToken);
        queue.UpdatedAt = DateTime.UtcNow;

        var senderName = senderPlayer.Player.User.Username;
        var queueTitle = !string.IsNullOrWhiteSpace(queue.Title) ? queue.Title : $"Hàng chờ #{queue.MatchmakingQueueId}";

        _notifications.Add(new NotificationInput(
            UserId: targetUserId,
            Type: NotificationTypes.Match,
            Title: "Lời mời tham gia hàng chờ ghép trận",
            Message: $"{senderName} đã mời bạn tham gia hàng chờ \"{queueTitle}\".",
            Tone: NotificationTones.Urgent,
            LinkTo: $"/opponents/queue/{queue.MatchmakingQueueId}",
            LinkLabel: "Xem hàng chờ"));
        await _matchRepository.SaveChangesAsync(cancellationToken);
        await SyncQueueToFirebaseAsync(queue, cancellationToken);
        if (queue.MatchId.HasValue) _matchRealtime.Publish(queue.MatchId.Value, "PlayersInvited");
        _notifications.PublishPending();

        return Ok(new { message = $"Đã gửi lời mời tham gia hàng chờ đến {targetUser.Username}." });
    }
}
