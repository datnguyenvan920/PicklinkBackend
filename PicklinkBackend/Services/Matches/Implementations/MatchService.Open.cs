using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches.Implementations;

public partial class MatchService
{
    public async Task<ServiceResult> CreateMatch(CreateMatchRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var player = await _matchRepository.Players.Include(item => item.User)
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (player is null) return BadRequest(new { message = "Không tìm thấy hồ sơ Người chơi cho tài khoản này." });

        if (request.MinSkillLevel > request.MaxSkillLevel)
            return BadRequest(new { message = "Trình độ tối thiểu không thể lớn hơn trình độ tối đa." });

        var requiredPlayerCount = request.RequiredPlayerCount > 0 ? request.RequiredPlayerCount : (request.MatchType == "1vs1" ? 2 : 4);
        if (requiredPlayerCount < 2 || requiredPlayerCount > 16)
            return BadRequest(new { message = "Số lượng người chơi phải từ 2 đến 16." });

        if (!string.IsNullOrWhiteSpace(request.PreferredTimeStart) && !string.IsNullOrWhiteSpace(request.PreferredTimeEnd))
        {
            if (TimeOnly.TryParse(request.PreferredTimeStart, out var pStart) && TimeOnly.TryParse(request.PreferredTimeEnd, out var pEnd))
            {
                var pStartMin = pStart.Hour * 60 + pStart.Minute;
                var pEndMin = (pEnd == TimeOnly.MinValue && pStart > TimeOnly.MinValue) ? 24 * 60 : pEnd.Hour * 60 + pEnd.Minute;

                if (pStartMin >= pEndMin)
                {
                    return BadRequest(new { message = "Giờ kết thúc dự kiến phải lớn hơn giờ bắt đầu dự kiến." });
                }
                if (pEndMin - pStartMin < 30)
                {
                    return BadRequest(new { message = "Khung giờ chơi dự kiến phải kéo dài ít nhất 30 phút." });
                }
            }
        }

        if (request.AvailabilitySlots is not null)
        {
            var parsed = request.AvailabilitySlots
                .Select(s => (
                    StartOk: TimeOnly.TryParse(s.TimeStart, out var st),
                    EndOk: TimeOnly.TryParse(s.TimeEnd, out var en),
                    StartMin: TimeOnly.TryParse(s.TimeStart, out var stVal) ? stVal.Hour * 60 + stVal.Minute : 0,
                    EndMin: TimeOnly.TryParse(s.TimeEnd, out var enVal)
                        ? ((enVal == TimeOnly.MinValue && stVal > TimeOnly.MinValue) ? 24 * 60 : enVal.Hour * 60 + enVal.Minute)
                        : 0,
                    StartStr: s.TimeStart,
                    EndStr: s.TimeEnd
                ))
                .Where(s => s.StartOk && s.EndOk && s.StartMin < s.EndMin)
                .OrderBy(s => s.StartMin)
                .ToList();

            var blocks = new List<(int StartMin, int EndMin, string StartStr, string EndStr)>();
            foreach (var item in parsed)
            {
                if (blocks.Count == 0)
                {
                    blocks.Add((item.StartMin, item.EndMin, item.StartStr, item.EndStr));
                }
                else
                {
                    var lastIndex = blocks.Count - 1;
                    var current = blocks[lastIndex];
                    if (item.StartMin <= current.EndMin)
                    {
                        if (item.EndMin > current.EndMin)
                        {
                            blocks[lastIndex] = (current.StartMin, item.EndMin, current.StartStr, item.EndStr);
                        }
                    }
                    else
                    {
                        blocks.Add((item.StartMin, item.EndMin, item.StartStr, item.EndStr));
                    }
                }
            }

            foreach (var block in blocks)
            {
                if (block.EndMin - block.StartMin < 30)
                {
                    return BadRequest(new { message = $"Chuỗi khung giờ chơi liên tục ({block.StartStr} - {block.EndStr}) phải kéo dài ít nhất 30 phút." });
                }
            }
        }

        var now = DateTime.UtcNow;
        var match = new Match
        {
            HostPlayerId = player.PlayerId,
            MatchType = request.MatchType,
            MatchSkillLevel = (int)Math.Round(player.SkillLevel),
            MinSkillLevel = request.MinSkillLevel,
            MaxSkillLevel = request.MaxSkillLevel,
            RequiredPlayerCount = requiredPlayerCount,
            Status = "Recruiting",
            Origin = "Community",
            Title = request.Title?.Trim(),
            Province = request.Province?.Trim() ?? string.Empty,
            Ward = request.Ward?.Trim() ?? string.Empty,
            SearchRadiusKm = request.SearchRadiusKm,
            SearchLatitude = request.SearchLatitude,
            SearchLongitude = request.SearchLongitude,
            SharedVenues = request.SharedVenues != null && request.SharedVenues.Count > 0 ? string.Join(",", request.SharedVenues) : null,
            ReplayType = request.ReplayType ?? "None",
            ReplayWeekdays = request.ReplayWeekdays != null && request.ReplayWeekdays.Count > 0 ? string.Join(",", request.ReplayWeekdays) : null,
            AvailableDateFrom = request.AvailableDateFrom,
            AvailableDateTo = request.AvailableDateTo,
            PreferredTimeStart = request.PreferredTimeStart is null ? null : TimeOnly.Parse(request.PreferredTimeStart),
            PreferredTimeEnd = request.PreferredTimeEnd is null ? null : TimeOnly.Parse(request.PreferredTimeEnd),
            CreatedAt = now
        };

        match.MatchParticipants.Add(new MatchParticipant
        {
            PlayerId = player.PlayerId,
            Status = "Approved",
            IsHost = true,
            RequestedAt = now,
            RespondedAt = now
        });

        if (request.AvailabilitySlots is not null)
        {
            foreach (var slot in request.AvailabilitySlots)
            {
                match.AvailabilitySlots.Add(new MatchAvailabilitySlot
                {
                    TimeStart = TimeOnly.Parse(slot.TimeStart),
                    TimeEnd = TimeOnly.Parse(slot.TimeEnd)
                });
            }
        }

        await _matchRepository.AddMatchAsync(match, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        var conversation = new Conversation
        {
            MatchId = match.MatchId,
            ConversationType = "LobbyChat",
            ConversationName = match.Title ?? $"Chat ghep tran {match.MatchId}",
            CreatedAt = now
        };
        await _matchRepository.AddConversationAsync(conversation, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        await _matchRepository.AddConversationParticipantAsync(new ConversationParticipant
        {
            ConversationId = conversation.ConversationId,
            UserId = userId,
            JoinedAt = now
        }, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        _matchRealtime.Publish(match.MatchId, "MatchCreated");

        var created = await GetMatchGraphAsync(match.MatchId, tracking: false, cancellationToken);
        return Ok();
    }

    public async Task<ServiceResult<PaginatedResponse<MatchSearchResponse>>> SearchOpenMatches(
        string? search,
        int? venueId,
        DateOnly? date,
        string? matchType,
        int? minSkillLevel,
        int? maxSkillLevel,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var query = _matchRepository.Matches.AsNoTracking();
        query = BaseMatchQuery(query);

        query = query.Where(match =>
            match.Status == "Recruiting" &&
            match.MatchParticipants.Count(participant =>
                participant.Status == "Approved" || participant.Status == "Accepted") < match.RequiredPlayerCount);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(match =>
                (match.Title != null && match.Title.Contains(keyword)) ||
                match.Bookings.Any(booking => booking.Court.Venue.VenueName.Contains(keyword)));
        }

        if (venueId.HasValue)
        {
            query = query.Where(match => match.Bookings.Any(booking => booking.Court.VenueId == venueId.Value));
        }

        if (date.HasValue)
        {
            var dayStart = date.Value.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(match => match.Bookings.Any(booking => booking.StartTime >= dayStart && booking.StartTime < dayEnd)
                || (match.AvailableDateFrom <= date.Value && match.AvailableDateTo >= date.Value));
        }

        if (!string.IsNullOrWhiteSpace(matchType))
        {
            query = query.Where(match => match.MatchType == matchType);
        }

        if (minSkillLevel.HasValue)
        {
            query = query.Where(match => match.MaxSkillLevel >= minSkillLevel.Value);
        }

        if (maxSkillLevel.HasValue)
        {
            query = query.Where(match => match.MinSkillLevel <= maxSkillLevel.Value);
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);

        var totalCount = await query.CountAsync(cancellationToken);
        var matches = await query
            .OrderByDescending(match => match.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(Pagination.Create(matches.Select(m => MapMatchResponse(m)).ToList(), totalCount, page, pageSize));
    }

    public async Task<ServiceResult<MatchSearchResponse>> GetMatchDetail(int matchId, CancellationToken cancellationToken)
    {
        var match = await GetMatchGraphAsync(matchId, tracking: false, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận đấu." });

        return Ok(MapMatchResponse(match));
    }

    public async Task<ServiceResult<MatchSearchResponse>> JoinMatch(int matchId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var player = await _matchRepository.Players.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (player is null) return BadRequest(new { message = "Không tìm thấy hồ sơ Người chơi." });

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Trận đấu đang được cập nhật. Vui lòng thử lại." });

        var match = await GetMatchGraphAsync(matchId, tracking: true, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận đấu." });

        if (match.Status is "Booked" or "Completed" or "Cancelled")
            return BadRequest(new { message = "Trận đấu không còn nhận thành viên mới." });

        var existingParticipant = match.MatchParticipants.FirstOrDefault(item => item.PlayerId == player.PlayerId);
        if (existingParticipant is not null && IsApprovedOrAccepted(existingParticipant.Status))
            return Conflict(new { message = "Bạn đã là thành viên của trận đấu này." });

        var approvedCount = match.MatchParticipants.Count(item => IsApprovedOrAccepted(item.Status));
        if (approvedCount >= match.RequiredPlayerCount)
            return BadRequest(new { message = "Trận đấu đã đủ số lượng người chơi." });

        if (player.SkillLevel < match.MinSkillLevel || player.SkillLevel > match.MaxSkillLevel)
            return BadRequest(new { message = $"Trình độ của bạn ({player.SkillLevel}) không phù hợp với trận đấu (Yêu cầu: {match.MinSkillLevel}-{match.MaxSkillLevel})." });

        var now = DateTime.UtcNow;
        if (existingParticipant is null)
        {
            match.MatchParticipants.Add(new MatchParticipant
            {
                PlayerId = player.PlayerId,
                Status = "Approved",
                IsHost = false,
                RequestedAt = now,
                RespondedAt = now
            });
        }
        else
        {
            existingParticipant.Status = "Approved";
            existingParticipant.RespondedAt = now;
        }

        var newApprovedCount = match.MatchParticipants.Count(item => IsApprovedOrAccepted(item.Status));
        if (newApprovedCount >= match.RequiredPlayerCount && match.Status == "Recruiting")
        {
            match.Status = "ReadyToBook";
        }

        await _matchRepository.SaveChangesAsync(cancellationToken);

        var conversation = await _matchRepository.Conversations.FirstOrDefaultAsync(item => item.MatchId == matchId && item.ConversationType == "LobbyChat", cancellationToken);
        if (conversation is not null)
        {
            var isChatParticipant = await _matchRepository.ConversationParticipants.AnyAsync(item => item.ConversationId == conversation.ConversationId && item.UserId == userId, cancellationToken);
            if (!isChatParticipant)
            {
                await _matchRepository.AddConversationParticipantAsync(new ConversationParticipant
                {
                    ConversationId = conversation.ConversationId,
                    UserId = userId,
                    JoinedAt = now
                }, cancellationToken);
                await _matchRepository.SaveChangesAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        _matchRealtime.Publish(matchId, "PlayerJoined");

        var updated = await GetMatchGraphAsync(matchId, tracking: false, cancellationToken);
        return Ok(MapMatchResponse(updated!));
    }

    public async Task<ServiceResult<MatchSearchResponse>> LeaveMatch(int matchId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var player = await _matchRepository.Players.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (player is null) return BadRequest(new { message = "Không tìm thấy hồ sơ Người chơi." });

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Trận đấu đang được cập nhật. Vui lòng thử lại." });

        var match = await GetMatchGraphAsync(matchId, tracking: true, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận đấu." });

        var participant = match.MatchParticipants.FirstOrDefault(item => item.PlayerId == player.PlayerId);
        if (participant is null || !IsApprovedOrAccepted(participant.Status))
            return BadRequest(new { message = "Bạn không phải là thành viên của trận đấu này." });

        if (participant.IsHost)
            return BadRequest(new { message = "Chủ phòng không thể rời trận đấu. Vui lòng hủy trận nếu muốn giải tán trận." });

        if (match.Status is "Booked" or "Completed")
            return BadRequest(new { message = "Trận đấu đã đặt sân hoặc đã hoàn thành, không thể rời trận." });

        participant.Status = "Left";
        participant.RespondedAt = DateTime.UtcNow;

        var approvedCount = match.MatchParticipants.Count(item => IsApprovedOrAccepted(item.Status));
        if (approvedCount < match.RequiredPlayerCount && match.Status == "ReadyToBook")
        {
            match.Status = "Recruiting";
        }

        await _matchRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _matchRealtime.Publish(matchId, "PlayerLeft");

        var updated = await GetMatchGraphAsync(matchId, tracking: false, cancellationToken);
        return Ok(MapMatchResponse(updated!));
    }

    public async Task<ServiceResult<OpenMatchDetailResponse>> MarkReadyToBook(
        int matchId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var player = await _matchRepository.Players
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Không tìm thấy hồ sơ Người chơi." });

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Trận đấu đang được cập nhật. Vui lòng thử lại." });

        var match = await GetMatchGraphAsync(matchId, tracking: true, cancellationToken);
        if (match is null)
            return NotFound(new { message = "Không tìm thấy trận đấu." });

        if (match.HostPlayerId != player.PlayerId)
            return Forbid(new { message = "Chỉ chủ phòng mới có thể chốt danh sách." });

        if (match.Status == "ReadyToBook")
        {
            await transaction.CommitAsync(cancellationToken);
            return Ok((await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken))!);
        }

        if (match.Status != "Recruiting")
            return Conflict(new { message = "Phòng không còn ở trạng thái tuyển thành viên." });

        var approvedCount = match.MatchParticipants.Count(item => IsApprovedOrAccepted(item.Status));
        if (approvedCount < match.RequiredPlayerCount)
            return Conflict(new { message = $"Phòng cần đủ {match.RequiredPlayerCount} thành viên trước khi đặt sân." });

        match.Status = "ReadyToBook";
        await _matchRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _matchRealtime.Publish(matchId, "ReadyToBook");
        return Ok((await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken))!);
    }

    public async Task<ServiceResult<OpenMatchDetailResponse>> CreateMatchBooking(
        int matchId,
        CreateMatchBookingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var player = await _matchRepository.Players
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (player is null)
            return BadRequest(new { message = "Không tìm thấy hồ sơ Người chơi cho tài khoản này." });

        if (request.Slots == null || request.Slots.Count == 0)
            return BadRequest(new { message = "Vui lòng chọn ít nhất một slot." });

        await using var transaction = await _matchRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Trận đấu đang được cập nhật. Vui lòng thử lại." });

        var match = await GetMatchGraphAsync(matchId, tracking: true, cancellationToken);
        if (match is null)
            return NotFound(new { message = "Không tìm thấy trận đấu." });

        var participant = match.MatchParticipants.FirstOrDefault(p => p.PlayerId == player.PlayerId);
        if (participant is null || !IsApprovedOrAccepted(participant.Status))
            return Forbid(new { message = "Bạn phải là thành viên chính thức của trận đấu để đặt sân." });

        if (match.Status is "Booked" or "Completed" or "Cancelled" or "Expired")
            return BadRequest(new { message = $"Trận đấu đang ở trạng thái {match.Status}, không thể tạo booking mới." });

        var parsedSlots = request.Slots.Select(s => (
            CourtId: s.CourtId,
            StartTime: s.StartTime,
            EndTime: s.EndTime
        )).OrderBy(s => s.StartTime).ThenBy(s => s.CourtId).ToList();

        var courtIds = parsedSlots.Select(s => s.CourtId).Distinct().ToList();
        var courts = await _matchRepository.Courts
            .Include(c => c.Venue)
            .Where(c => courtIds.Contains(c.CourtId))
            .ToListAsync(cancellationToken);

        if (courts.Count != courtIds.Count)
            return NotFound(new { message = "Không tìm thấy sân con." });

        var venue = courts[0].Venue;
        var courtsById = courts.ToDictionary(c => c.CourtId);

        var holdMinutes = Math.Clamp(_configuration.GetValue("Match:PaymentMinutes", 30), 1, 120);
        var utcNow = DateTime.UtcNow;
        var firstStart = parsedSlots.Min(s => s.StartTime);
        var lastEnd = parsedSlots.Max(s => s.EndTime);

        if (!request.AllowScheduleConflicts)
        {
            var approvedPlayerIds = match.MatchParticipants
                .Where(p => IsApprovedOrAccepted(p.Status))
                .Select(p => p.PlayerId)
                .ToList();

            var conflictList = new List<object>();
            foreach (var pid in approvedPlayerIds)
            {
                var pUser = await _matchRepository.Players
                    .Include(p => p.User)
                    .SingleOrDefaultAsync(p => p.PlayerId == pid, cancellationToken);
                var pName = pUser?.User?.Username ?? $"Người chơi #{pid}";

                var conflictDetails = await _playerScheduleConflict.LoadConflictDetailsAsync(
                    pid, firstStart, lastEnd, cancellationToken: cancellationToken);

                foreach (var slot in parsedSlots)
                {
                    foreach (var conflict in conflictDetails.Where(c => c.StartTime < slot.EndTime && c.EndTime > slot.StartTime))
                    {
                        conflictList.Add(new
                        {
                            playerName = pName,
                            selectedSlot = new
                            {
                                venueName = venue.VenueName,
                                courtNumber = courtsById[slot.CourtId].CourtNumber,
                                startTime = slot.StartTime,
                                endTime = slot.EndTime
                            },
                            conflictingSlot = conflict
                        });
                    }
                }
            }

            if (conflictList.Count > 0)
            {
                return Conflict(new
                {
                    message = "Thành viên trong phòng đã có lịch trùng với slot được chọn.",
                    requiresScheduleConflictConfirmation = true,
                    conflicts = conflictList.Distinct()
                });
            }
        }

        var overlappingBookings = await _matchRepository.Bookings
            .Include(b => b.Slots)
            .Where(b => courtIds.Contains(b.CourtId) || b.Slots.Any(s => courtIds.Contains(s.CourtId)))
            .Where(b => (b.Status == "Holding" && b.HoldExpiresAt > utcNow) || b.Status == "Confirmed" || b.Status == "Completed")
            .Where(b => b.MatchId != matchId)
            .ToListAsync(cancellationToken);

        var overlaps = overlappingBookings.Any(b => parsedSlots.Any(s =>
            b.Slots.Any(bs => bs.CourtId == s.CourtId && bs.StartTime < s.EndTime && bs.EndTime > s.StartTime)
            || (!b.Slots.Any() && b.CourtId == s.CourtId && b.StartTime < s.EndTime && b.EndTime > s.StartTime)));

        if (overlaps)
            return Conflict(new { message = "Một hoặc nhiều slot vừa được người khác giữ hoặc đặt. Vui lòng chọn slot khác." });

        var totalAmount = parsedSlots.Sum(s =>
        {
            var court = courtsById[s.CourtId];
            var hourly = court.HourlyPrice > 0 ? court.HourlyPrice : 100000m;
            return Math.Round(hourly * (decimal)(s.EndTime - s.StartTime).TotalHours, 0);
        });

        var parentSlot = parsedSlots[0];
        var parentCourt = courtsById[parentSlot.CourtId];

        var booking = new Booking
        {
            PlayerId = player.PlayerId,
            CourtId = parentCourt.CourtId,
            MatchId = match.MatchId,
            StartTime = firstStart,
            EndTime = lastEnd,
            Status = "Holding",
            BookingCode = $"PL-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            CreatedAt = utcNow,
            HoldExpiresAt = utcNow.AddMinutes(holdMinutes),
            HourlyPriceSnapshot = parentCourt.HourlyPrice > 0 ? parentCourt.HourlyPrice : 100000m,
            CourtAmount = totalAmount,
            TotalAmount = totalAmount
        };

        BookingCheckInGroup? currentCheckInGroup = null;
        foreach (var selectedSlot in parsedSlots)
        {
            var selectedCourt = courtsById[selectedSlot.CourtId];
            var startsNewCheckInGroup = currentCheckInGroup is null
                || currentCheckInGroup.CourtId != selectedSlot.CourtId
                || currentCheckInGroup.EndTime != selectedSlot.StartTime;

            if (startsNewCheckInGroup)
            {
                currentCheckInGroup = new BookingCheckInGroup
                {
                    CourtId = selectedSlot.CourtId,
                    Court = selectedCourt,
                    StartTime = selectedSlot.StartTime,
                    EndTime = selectedSlot.EndTime,
                    CheckInCode = $"CI-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                    UpdatedAt = utcNow
                };
                booking.CheckInGroups.Add(currentCheckInGroup);
            }
            else if (currentCheckInGroup is not null)
            {
                currentCheckInGroup.EndTime = selectedSlot.EndTime;
            }

            var durationHours = (selectedSlot.EndTime - selectedSlot.StartTime).TotalHours;
            var hourlyPrice = selectedCourt.HourlyPrice > 0 ? selectedCourt.HourlyPrice : 100000m;
            booking.Slots.Add(new BookingSlot
            {
                CourtId = selectedCourt.CourtId,
                Court = selectedCourt,
                StartTime = selectedSlot.StartTime,
                EndTime = selectedSlot.EndTime,
                HourlyPriceSnapshot = hourlyPrice,
                CourtAmount = Math.Round(hourlyPrice * (decimal)durationHours, 0),
                CheckInGroup = currentCheckInGroup
            });
        }

        var approvedParticipants = match.MatchParticipants
            .Where(p => IsApprovedOrAccepted(p.Status))
            .ToList();
        var payerCount = Math.Max(1, approvedParticipants.Count);
        var amountPerPlayer = Math.Round(totalAmount / payerCount, 0);

        var bankAccount = await _matchRepository.OwnerBankAccounts
            .FirstOrDefaultAsync(b => b.OwnerId == venue.OwnerId && b.IsActive, cancellationToken);
        var paymentGroupId = Guid.NewGuid();
        var groupTransferContent = $"PLG-{paymentGroupId:N}"[..20].ToUpperInvariant();

        foreach (var p in approvedParticipants)
        {
            var pPayment = new Payment
            {
                PayerId = p.PlayerId,
                PaymentGroupId = paymentGroupId,
                Amount = amountPerPlayer,
                PaymentMethod = "BankTransfer",
                Status = "Pending",
                TransferCode = $"PL{DateTime.UtcNow:yyyyMMdd}{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                TransferContent = groupTransferContent,
                BankCode = bankAccount?.BankCode,
                BankName = bankAccount?.BankName,
                BankAccountNumber = bankAccount?.AccountNumber,
                BankAccountName = bankAccount?.AccountHolderName,
                QrImageUrl = bankAccount is null ? null : BuildVietQrUrl(bankAccount, amountPerPlayer, groupTransferContent)
            };
            booking.Payments.Add(pPayment);
        }

        await _matchRepository.AddBookingAsync(booking, cancellationToken);

        match.Status = "BookingPending";
        match.AvailableDateFrom = DateOnly.FromDateTime(firstStart);
        match.AvailableDateTo = DateOnly.FromDateTime(lastEnd);
        match.PreferredTimeStart = TimeOnly.FromDateTime(firstStart);
        match.PreferredTimeEnd = TimeOnly.FromDateTime(lastEnd);

        await _matchRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var slot in booking.Slots)
        {
            _scheduleRealtime.Publish(new ScheduleChangedEvent(venue.VenueId, slot.CourtId, slot.StartTime, slot.EndTime, "Holding", "Created"));
        }
        _matchRealtime.Publish(matchId, "BookingCreated");

        var response = await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken);
        return Ok(response!);
    }
}
