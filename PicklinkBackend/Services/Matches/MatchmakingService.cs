using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches;

public class MatchmakingService
{
    private readonly ApplicationDbContext _db;
    private int? _currentUserId;

    public MatchmakingService(ApplicationDbContext db)
    {
        _db = db;
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
            foreach (var slot in request.QueueSlots)
                Validator.TryValidateObject(slot, new ValidationContext(slot), validationResults, validateAllProperties: true);
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

    private static bool IsApproved(MatchmakingQueuePlayer queuePlayer) => queuePlayer.Status == "Approved";

    public async Task<ServiceResult<QueueStatusResponse>> GetQueueStatus(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return Ok(new QueueStatusResponse { InQueue = false });

        var queueItem = await _db.MatchmakingQueues
            .Include(q => q.QueueSlots)
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .Include(q => q.Conversations)
            .FirstOrDefaultAsync(q => q.QueuePlayers.Any(qp => qp.PlayerId == player.PlayerId && qp.Status == "Approved"), cancellationToken);

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

        var player = await _db.Players.Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });


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

        // Add player to queue party
        queueItem.QueuePlayers.Add(new MatchmakingQueuePlayer
        {
            PlayerId = player.PlayerId,
            IsHost = true
        });

        // Parse slots
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

        _db.MatchmakingQueues.Add(queueItem);
        await _db.SaveChangesAsync(cancellationToken);

        // Create Chat Conversation
        var conversation = new Conversation
        {
            MatchmakingQueueId = queueItem.MatchmakingQueueId,
            ConversationType = "QueueLobbyChat",
            ConversationName = $"Hàng chờ - {player.User.Username}",
            CreatedAt = DateTime.UtcNow
        };
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(cancellationToken);

        // Add chat participant
        _db.ConversationParticipants.Add(new ConversationParticipant
        {
            ConversationId = conversation.ConversationId,
            UserId = player.UserId,
            JoinedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        return await GetQueueStatus(cancellationToken);
    }

    public async Task<ServiceResult<QueueStatusResponse>> JoinLobbyQueue(int matchId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        var match = await _db.Matches
            .Include(m => m.AvailabilitySlots)
            .Include(m => m.MatchParticipants).ThenInclude(mp => mp.Player).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(m => m.MatchId == matchId, cancellationToken);

        if (match is null)
            return NotFound(new { message = "Không tìm thấy phòng ghép trận." });

        if (match.HostPlayerId != player.PlayerId)
            return Forbidden(new { message = "Chỉ chủ phòng mới được đưa phòng vào hàng chờ." });

        if (match.Status != "Recruiting")
            return BadRequest(new { message = "Phòng ghép trận không ở trạng thái tuyển người." });

        // Extract target weekdays for validation
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
            IsPublic = false, // Defaults to false as requested
            Province = match.Province,
            Ward = match.Ward,
            SharedVenues = match.SharedVenues,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        // Add all approved lobby participants to the queue ticket
        var participants = match.MatchParticipants.Where(mp => mp.Status == "Approved").ToList();
        foreach (var p in participants)
        {
            queueItem.QueuePlayers.Add(new MatchmakingQueuePlayer
            {
                PlayerId = p.PlayerId,
                IsHost = p.PlayerId == match.HostPlayerId
            });
        }

        // Map Slots
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
                    var fromDate = match.AvailableDateFrom ?? DateOnly.FromDateTime(DateTime.Today);
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
            var fromDate = match.AvailableDateFrom ?? DateOnly.FromDateTime(DateTime.Today);
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

        _db.MatchmakingQueues.Add(queueItem);
        await _db.SaveChangesAsync(cancellationToken);

        // Create Chat Conversation for the Queue
        var conversation = new Conversation
        {
            MatchmakingQueueId = queueItem.MatchmakingQueueId,
            ConversationType = "QueueLobbyChat",
            ConversationName = $"Hàng chờ - {match.Title}",
            CreatedAt = DateTime.UtcNow
        };
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(cancellationToken);

        // Add all party players as chat participants
        foreach (var qp in queueItem.QueuePlayers)
        {
            var p = participants.First(part => part.PlayerId == qp.PlayerId);
            _db.ConversationParticipants.Add(new ConversationParticipant
            {
                ConversationId = conversation.ConversationId,
                UserId = p.Player.UserId,
                JoinedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync(cancellationToken);

        return await GetQueueStatus(cancellationToken);
    }

    public async Task<ServiceResult<List<QueueStatusResponse>>> GetMyQueues(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return Ok(new List<QueueStatusResponse>());

        var queues = await _db.MatchmakingQueues
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

        var player = await _db.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return NotFound(new { message = "Hồ sơ người chơi không tồn tại." });

        var query = _db.MatchmakingQueues.Include(q => q.QueuePlayers).AsQueryable();
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
                    // Host disbands the entire queue ticket
                    await DeleteMatchmakingQueues(new List<MatchmakingQueue> { queueItem }, cancellationToken);
                }
                else
                {
                    // Non-host player simply leaves the queue party
                    _db.MatchmakingQueuePlayers.Remove(qpEntry);

                    // Remove them from the queue chat
                    var conversation = await _db.Conversations
                        .FirstOrDefaultAsync(c => c.MatchmakingQueueId == queueItem.MatchmakingQueueId && c.ConversationType == "QueueLobbyChat", cancellationToken);
                    if (conversation is not null)
                    {
                        var convParticipant = await _db.ConversationParticipants
                            .FirstOrDefaultAsync(cp => cp.ConversationId == conversation.ConversationId && cp.UserId == player.UserId, cancellationToken);
                        if (convParticipant is not null)
                        {
                            _db.ConversationParticipants.Remove(convParticipant);
                        }
                    }
                }

                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return Ok(new { message = "Đã rời hàng chờ ghép trận." });
    }

    public async Task<ServiceResult<QueueStatusResponse>> ResumeQueue(int? queueId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return NotFound(new { message = "Hồ sơ người chơi không tồn tại." });

        var query = _db.MatchmakingQueues
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

        queueItem.IsActive = true;
        queueItem.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

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

        var queues = await _db.MatchmakingQueues
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .Include(q => q.QueueSlots)
            .Where(q => q.IsActive && q.IsPublic)
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
            SearchLatitude = q.SearchLatitude,
            MinSkillLevel = q.MinSkillLevel,
            MaxSkillLevel = q.MaxSkillLevel,
            SearchLongitude = q.SearchLongitude,
            IsActive = q.IsActive,
            ReplayType = q.ReplayType,
            ConversationId = _db.Conversations.FirstOrDefault(c => c.MatchmakingQueueId == q.MatchmakingQueueId && c.ConversationType == "QueueLobbyChat")?.ConversationId,
            IsPublic = q.IsPublic,
            Province = q.Province,
            Ward = q.Ward,
            SharedVenues = q.SharedVenues,
            UpdatedAt = EnsureUtcKind(q.UpdatedAt),
            CreatedAt = EnsureUtcKind(q.CreatedAt),
            QueuePlayers = q.QueuePlayers.Select(qp => new QueuePlayerResponse
            {
                PlayerId = qp.PlayerId,
                PlayerName = qp.Player.User.Username,
                AvatarUrl = qp.Player.User.ProfileImageUrl,
                IsHost = qp.IsHost,
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

    public async Task<ServiceResult<QueueStatusResponse>> JoinPublicQueue(int queueId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        var targetQueue = await _db.MatchmakingQueues
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .Include(q => q.QueueSlots)
            .FirstOrDefaultAsync(q => q.MatchmakingQueueId == queueId, cancellationToken);

        if (targetQueue is null)
            return NotFound(new { message = "Không tìm thấy hàng chờ này." });

        if (!targetQueue.IsActive)
            return BadRequest(new { message = "Hàng chờ này đã bị tạm dừng." });

        if (!targetQueue.IsPublic)
            return BadRequest(new { message = "Hàng chờ này không công khai." });

        var maxCapacity = targetQueue.PlayerCount;
        if (targetQueue.QueuePlayers.Count(qp => qp.Status != "Rejected") >= maxCapacity)
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
        _db.MatchmakingQueuePlayers.Add(queuePlayer);
        targetQueue.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new QueueStatusResponse { InQueue = false, MatchmakingQueueId = queueId });
    }

    public async Task<ServiceResult<object>> CreateManualQueueRoom(int queueId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var queue = await _db.MatchmakingQueues
            .Include(item => item.QueueSlots)
            .Include(item => item.QueuePlayers).ThenInclude(item => item.Player)
            .SingleOrDefaultAsync(item => item.MatchmakingQueueId == queueId, cancellationToken);
        if (queue is null) return NotFound(new { message = "Không tìm thấy hàng chờ ghép trận." });
        if (!queue.IsPublic) return BadRequest(new { message = "Chỉ hàng chờ ghép thủ công mới có thể mở phòng." });
        if (!queue.QueuePlayers.Any(item => item.Player.UserId == userId && item.Status == "Approved"))
            return Forbidden(new { message = "Bạn không thuộc hàng chờ này." });
        if (queue.MatchId is int existingMatchId)
            return Ok(new { matchId = existingMatchId });

        var players = queue.QueuePlayers.Where(item => item.Status == "Approved").ToList();
        var host = players.FirstOrDefault(item => item.IsHost);
        if (host is null) return BadRequest(new { message = "Hàng chờ không có chủ phòng." });

        var slots = queue.QueueSlots.OrderBy(item => item.TimeStart).DistinctBy(item => (item.TimeStart, item.TimeEnd)).ToList();
        var date = slots.Where(item => item.SpecificDate.HasValue).Select(item => item.SpecificDate!.Value).DefaultIfEmpty(DateOnly.FromDateTime(DateTime.Today)).Min();
        var start = slots.FirstOrDefault()?.TimeStart ?? new TimeOnly(18, 0);
        var end = slots.LastOrDefault()?.TimeEnd ?? new TimeOnly(20, 0);
        var now = DateTime.UtcNow;
        var match = new Match
        {
            HostPlayerId = host.PlayerId,
            MatchType = queue.MatchType,
            MatchSkillLevel = queue.SkillLevel,
            MinSkillLevel = queue.MinSkillLevel,
            MaxSkillLevel = queue.MaxSkillLevel,
            RequiredPlayerCount = Math.Max(queue.PlayerCount, players.Count),
            Status = players.Count >= queue.PlayerCount ? "ReadyToBook" : "Recruiting",
            Title = queue.Title,
            Province = queue.Province ?? string.Empty,
            Ward = queue.Ward ?? string.Empty,
            SearchRadiusKm = queue.SearchRadiusKm,
            SearchLatitude = queue.SearchLatitude,
            SearchLongitude = queue.SearchLongitude,
            SharedVenues = queue.SharedVenues,
            AvailableDateFrom = date,
            AvailableDateTo = date,
            PreferredTimeStart = start,
            PreferredTimeEnd = end,
            CreatedAt = now
        };
        _db.Matches.Add(match);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var slot in slots)
            _db.MatchAvailabilitySlots.Add(new MatchAvailabilitySlot { MatchId = match.MatchId, TimeStart = slot.TimeStart, TimeEnd = slot.TimeEnd });
        foreach (var player in players)
            _db.MatchParticipants.Add(new MatchParticipant { MatchId = match.MatchId, PlayerId = player.PlayerId, Status = "Approved", IsHost = player.IsHost, RequestedAt = now, RespondedAt = now });

        var conversation = new Conversation { MatchId = match.MatchId, ConversationType = "LobbyChat", ConversationName = match.Title, CreatedAt = now };
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(cancellationToken);
        foreach (var player in players)
            _db.ConversationParticipants.Add(new ConversationParticipant { ConversationId = conversation.ConversationId, UserId = player.Player.UserId, JoinedAt = now });

        queue.MatchId = match.MatchId;
        queue.IsActive = false;
        queue.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new { matchId = match.MatchId });
    }
    public async Task<ServiceResult> ReviewPublicQueueRequest(int queueId, int playerId, bool approve, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var targetQueue = await _db.MatchmakingQueues
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

        if (approve)
        {
            var conversation = await _db.Conversations
                .FirstOrDefaultAsync(c => c.MatchmakingQueueId == queueId && c.ConversationType == "QueueLobbyChat", cancellationToken);
            if (conversation is not null)
            {
                var isParticipant = await _db.ConversationParticipants
                    .AnyAsync(cp => cp.ConversationId == conversation.ConversationId && cp.UserId == request.Player.UserId, cancellationToken);
                if (!isParticipant)
                    _db.ConversationParticipants.Add(new ConversationParticipant { ConversationId = conversation.ConversationId, UserId = request.Player.UserId, JoinedAt = DateTime.UtcNow });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = approve ? "Đã chấp nhận yêu cầu tham gia." : "Đã từ chối yêu cầu tham gia." });
    }

    private async Task DeleteMatchmakingQueues(List<MatchmakingQueue> queues, CancellationToken cancellationToken)
    {
        if (queues == null || queues.Count == 0) return;

        var queueIds = queues.Select(q => q.MatchmakingQueueId).ToList();

        // 1. Find all conversations associated with these queues
        var conversations = await _db.Conversations
            .Where(c => c.MatchmakingQueueId.HasValue && queueIds.Contains(c.MatchmakingQueueId.Value))
            .ToListAsync(cancellationToken);

        if (conversations.Count > 0)
        {
            var conversationIds = conversations.Select(c => c.ConversationId).ToList();

            // 2. Find and delete all conversation participants
            var participants = await _db.ConversationParticipants
                .Where(cp => conversationIds.Contains(cp.ConversationId))
                .ToListAsync(cancellationToken);
            if (participants.Count > 0)
            {
                _db.ConversationParticipants.RemoveRange(participants);
            }

            // 3. Find and delete all messages
            var messages = await _db.Messages
                .Where(m => conversationIds.Contains(m.ConversationId))
                .ToListAsync(cancellationToken);
            if (messages.Count > 0)
            {
                _db.Messages.RemoveRange(messages);
            }

            // 4. Delete the conversations
            _db.Conversations.RemoveRange(conversations);
        }

        // 5. Delete the queues
        _db.MatchmakingQueues.RemoveRange(queues);
    }

    private static DateTime? EnsureUtcKind(DateTime? dateTime)
    {
        if (!dateTime.HasValue) return null;
        return DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc);
    }
}
