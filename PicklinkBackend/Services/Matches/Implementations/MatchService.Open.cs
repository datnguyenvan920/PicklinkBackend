using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
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

        query = query.Where(match => match.Status == "Recruiting" || match.Status == "ReadyToBook");

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
}
