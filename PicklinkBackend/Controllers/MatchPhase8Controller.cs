using System.Data;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

public partial class MatchController
{
    private static readonly string[] MatchInactiveBookingStatuses = ["Cancelled", "Expired"];

    [Authorize]
    [HttpPost]
    [HttpPost("open")]
    public async Task<ActionResult<OpenMatchDetailResponse>> CreateOpenMatch(
        CreateOpenMatchRequest request,
        CancellationToken cancellationToken)
    {
        var player = await CurrentPlayerAsync(cancellationToken);
        if (player is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        var matchType = NormalizeMatchType(request.MatchType);
        if (matchType is null)
            return BadRequest(new { message = "Hình thức trận chỉ nhận 1vs1 hoặc 2vs2." });
        if (request.StartTime <= DateTime.Now)
            return BadRequest(new { message = "Thời gian bắt đầu phải ở tương lai." });
        if (request.EndTime <= request.StartTime)
            return BadRequest(new { message = "Thời gian kết thúc phải sau thời gian bắt đầu." });
        if (request.EndTime - request.StartTime > TimeSpan.FromHours(4))
            return BadRequest(new { message = "Một trận không được dài quá 4 giờ." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"court-booking:{request.CourtId}", cancellationToken))
            return Conflict(new { message = "Sân đang được người khác thao tác. Vui lòng thử lại." });
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"player-schedule:{player.PlayerId}", cancellationToken))
            return Conflict(new { message = "Lịch của bạn đang được cập nhật. Vui lòng thử lại." });

        var court = await _db.Courts
            .Include(item => item.Venue).ThenInclude(item => item.BookingRules)
            .SingleOrDefaultAsync(item => item.CourtId == request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });
        if (!court.Venue.IsOpen || court.AvailabilityStatus != "Available")
            return Conflict(new { message = "Sân hiện không nhận đặt lịch." });

        var date = DateOnly.FromDateTime(request.StartTime);
        if (request.StartTime < date.ToDateTime(court.Venue.OpenTime)
            || request.EndTime > date.ToDateTime(court.Venue.CloseTime))
            return BadRequest(new { message = $"Khung giờ phải nằm trong giờ mở cửa {court.Venue.OpenTime:HH:mm}–{court.Venue.CloseTime:HH:mm}." });

        var now = DateTime.UtcNow;
        if (await _playerScheduleConflict.HasConflictAsync(
                player.PlayerId,
                request.StartTime,
                request.EndTime,
                cancellationToken: cancellationToken))
            return Conflict(new { message = "Bạn đã có lịch đặt sân hoặc ghép trận trùng với khung giờ này." });

        var overlaps = await _db.Bookings.AnyAsync(booking =>
            booking.CourtId == request.CourtId
            && !MatchInactiveBookingStatuses.Contains(booking.Status)
            && (booking.Status != "Holding" || booking.HoldExpiresAt > now)
            && booking.StartTime < request.EndTime
            && booking.EndTime > request.StartTime,
            cancellationToken);
        if (overlaps) return Conflict(new { message = "Khung giờ này vừa được giữ hoặc đã được đặt." });

        var requiredPlayerCount = matchType == "1vs1" ? 2 : 4;
        var matchingMinutes = MatchMatchingMinutes();
        var hourlyPrice = court.HourlyPrice > 0 ? court.HourlyPrice : MatchVenueBasePrice(court.Venue);
        if (hourlyPrice <= 0)
            return Conflict(new { message = "Sân chưa được thiết lập giá theo giờ. Vui lòng liên hệ chủ sân." });
        var totalAmount = Math.Round(hourlyPrice * (request.EndTime - request.StartTime).TotalHours, 0, MidpointRounding.AwayFromZero);
        var match = new Match
        {
            HostPlayerId = player.PlayerId,
            MatchType = matchType,
            MatchSkillLevel = request.MatchSkillLevel,
            RequiredPlayerCount = requiredPlayerCount,
            MatchTime = request.StartTime,
            Status = "Waiting",
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAt = now
        };
        match.MatchParticipants.Add(new MatchParticipant
        {
            PlayerId = player.PlayerId,
            Status = "Accepted",
            IsHost = true,
            RequestedAt = now,
            RespondedAt = now
        });

        var booking = new Booking
        {
            PlayerId = player.PlayerId,
            CourtId = court.CourtId,
            Match = match,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = "MatchWaiting",
            Title = $"{matchType} - {court.Venue.VenueName}",
            BookingCode = $"PM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            CreatedAt = now,
            HoldExpiresAt = now.AddMinutes(matchingMinutes),
            HourlyPriceSnapshot = hourlyPrice,
            CourtAmount = totalAmount,
            TotalAmount = totalAmount
        };
        booking.StatusHistories.Add(NewMatchBookingHistory(null, "MatchWaiting", "Chủ trận tạo trận ghép", CurrentUserIdPhase8()));
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            court.VenueId, court.CourtId, booking.StartTime, booking.EndTime, booking.Status, "Created"));
        _matchRealtime.Publish(match.MatchId, "Created");

        return CreatedAtAction(nameof(GetOpenMatchDetail), new { matchId = match.MatchId },
            await LoadOpenMatchResponseAsync(match.MatchId, player.PlayerId, cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("open")]
    public async Task<ActionResult<PaginatedResponse<MatchSearchResponse>>> GetOpenMatches(
        string? matchType,
        int? skillLevel,
        DateTime? from,
        DateTime? to,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = string.IsNullOrWhiteSpace(matchType) ? null : NormalizeMatchType(matchType);
        if (!string.IsNullOrWhiteSpace(matchType) && normalizedType is null)
            return BadRequest(new { message = "Hình thức trận chỉ nhận 1vs1 hoặc 2vs2." });
        if (skillLevel is < 1 or > 5)
            return BadRequest(new { message = "Trình độ phải từ 1 đến 5." });

        var currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        var query = MatchPhase8Query(asNoTracking: true)
            .Where(item => item.HostPlayerId != null
                && item.Bookings.Any()
                && item.Status == "Waiting"
                && item.MatchTime > DateTime.Now);
        if (normalizedType is not null) query = query.Where(item => item.MatchType == normalizedType);
        if (skillLevel.HasValue) query = query.Where(item => item.MatchSkillLevel == skillLevel.Value);
        if (from.HasValue) query = query.Where(item => item.MatchTime >= from.Value);
        if (to.HasValue) query = query.Where(item => item.MatchTime <= to.Value);

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var matches = await query
            .OrderBy(item => item.MatchTime)
            .ThenBy(item => item.MatchId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(Pagination.Create(matches.Select(item => MapSearchResponse(item, currentPlayerId)), totalCount, page, pageSize));
    }

    [Authorize]
    [HttpGet("mine")]
    public async Task<ActionResult<PaginatedResponse<MatchSearchResponse>>> GetMyPhase8Matches(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        if (playerId is null) return Ok(Pagination.Create(Array.Empty<MatchSearchResponse>(), 0, page, pageSize));

        var matches = await MatchPhase8Query(asNoTracking: true)
            .Where(item => item.HostPlayerId != null
                && item.Bookings.Any()
                && item.MatchParticipants.Any(participant =>
                participant.PlayerId == playerId
                && participant.Status != "Rejected"
                && participant.Status != "Left"
                && participant.Status != "Removed"))
            .OrderByDescending(item => item.MatchTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var totalCount = await MatchPhase8Query(asNoTracking: true)
            .Where(item => item.HostPlayerId != null
                && item.Bookings.Any()
                && item.MatchParticipants.Any(participant =>
                participant.PlayerId == playerId
                && participant.Status != "Rejected"
                && participant.Status != "Left"
                && participant.Status != "Removed"))
            .CountAsync(cancellationToken);
        return Ok(Pagination.Create(matches.Select(item => MapSearchResponse(item, playerId)), totalCount, page, pageSize));
    }

    [AllowAnonymous]
    [HttpGet("{matchId:int}")]
    public async Task<ActionResult<OpenMatchDetailResponse>> GetOpenMatchDetail(
        int matchId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        var response = await LoadOpenMatchResponseAsync(matchId, playerId, cancellationToken);
        return response is null ? NotFound(new { message = "Không tìm thấy trận." }) : Ok(response);
    }

    [Authorize]
    [HttpPost("{matchId:int}/join")]
    public async Task<ActionResult<OpenMatchDetailResponse>> JoinOpenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var player = await CurrentPlayerAsync(cancellationToken);
        if (player is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách người chơi đang được cập nhật." });
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"player-schedule:{player.PlayerId}", cancellationToken))
            return Conflict(new { message = "Lịch của bạn đang được cập nhật. Vui lòng thử lại." });

        var match = await MatchPhase8Query().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận." });
        if (match.HostPlayerId == player.PlayerId)
            return Conflict(new { message = "Bạn là chủ trận." });
        if (match.Status != "Waiting")
            return Conflict(new { message = "Trận hiện không nhận thêm yêu cầu tham gia." });
        if (AcceptedParticipants(match).Count >= match.RequiredPlayerCount)
            return Conflict(new { message = "Trận đã đủ người." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.PlayerId == player.PlayerId);
        if (participant?.Status is "Accepted" or "Pending")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Ok(await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken));
        }

        var booking = MatchBooking(match);
        if (await _playerScheduleConflict.HasConflictAsync(
                player.PlayerId,
                booking.StartTime,
                booking.EndTime,
                excludedMatchId: match.MatchId,
                cancellationToken: cancellationToken))
            return Conflict(new { message = "Bạn đã có lịch đặt sân hoặc ghép trận trùng với thời gian của trận này." });

        if (participant is null)
        {
            participant = new MatchParticipant
            {
                MatchId = match.MatchId,
                PlayerId = player.PlayerId,
                Status = "Pending",
                RequestedAt = DateTime.UtcNow
            };
            _db.MatchParticipants.Add(participant);
        }
        else
        {
            participant.Status = "Pending";
            participant.IsHost = false;
            participant.RequestedAt = DateTime.UtcNow;
            participant.RespondedAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "JoinRequested");
        return Ok(await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/leave")]
    public async Task<ActionResult<OpenMatchDetailResponse>> LeaveOpenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách người chơi đang được cập nhật." });

        var match = await MatchPhase8Query().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận." });
        if (match.HostPlayerId == playerId)
            return Conflict(new { message = "Chủ trận cần hủy trận thay vì rời trận." });
        if (HasStarted(match))
            return Conflict(new { message = "Không thể rời trận sau khi trận đã bắt đầu." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.PlayerId == playerId);
        if (participant is null || participant.Status is "Left" or "Rejected" or "Removed")
            return Conflict(new { message = "Bạn không ở trong danh sách trận." });
        if (participant.Status == "Accepted" && HasSettledPayment(match))
            return Conflict(new { message = "Đã có giao dịch được thanh toán; cần xử lý hoàn tiền trước khi thay đổi đội hình." });

        var wasAccepted = participant.Status == "Accepted";
        participant.Status = "Left";
        participant.RespondedAt = DateTime.UtcNow;
        if (wasAccepted) ResetRosterToWaiting(match, "Thành viên rời trận");

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "ParticipantLeft");
        return Ok(await LoadOpenMatchResponseAsync(matchId, playerId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/participants/{participantId:int}/accept")]
    public async Task<ActionResult<OpenMatchDetailResponse>> AcceptParticipant(
        int matchId,
        int participantId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách người chơi đang được cập nhật." });

        var match = await MatchPhase8Query().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (HasStarted(match))
            return Conflict(new { message = "Không thể duyệt thành viên sau khi trận đã bắt đầu." });
        if (match.Status != "Waiting")
            return Conflict(new { message = "Chỉ có thể duyệt thành viên khi trận đang chờ người." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.ParticipantId == participantId);
        if (participant is null || participant.Status != "Pending")
            return Conflict(new { message = "Yêu cầu tham gia không còn ở trạng thái chờ duyệt." });
        if (AcceptedParticipants(match).Count >= match.RequiredPlayerCount)
            return Conflict(new { message = "Trận đã đủ số người cần." });
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"player-schedule:{participant.PlayerId}", cancellationToken))
            return Conflict(new { message = "Lịch của người chơi đang được cập nhật. Vui lòng thử lại." });

        var booking = MatchBooking(match);
        if (await _playerScheduleConflict.HasConflictAsync(
                participant.PlayerId,
                booking.StartTime,
                booking.EndTime,
                excludedMatchId: match.MatchId,
                cancellationToken: cancellationToken))
            return Conflict(new { message = "Người chơi đã có lịch đặt sân hoặc ghép trận trùng với thời gian của trận này." });

        participant.Status = "Accepted";
        participant.RespondedAt = DateTime.UtcNow;
        if (AcceptedParticipants(match).Count == match.RequiredPlayerCount)
        {
            match.Status = "Full";
            await PrepareSplitPaymentsAsync(match, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "ParticipantAccepted");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/participants/{participantId:int}/reject")]
    public async Task<ActionResult<OpenMatchDetailResponse>> RejectParticipant(
        int matchId,
        int participantId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });
        var match = await MatchPhase8Query().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (HasStarted(match))
            return Conflict(new { message = "Chủ trận không được xóa người sau khi trận bắt đầu." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.ParticipantId == participantId);
        if (participant is null || participant.Status != "Pending")
            return Conflict(new { message = "Yêu cầu tham gia không còn ở trạng thái chờ duyệt." });
        participant.Status = "Rejected";
        participant.RespondedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "ParticipantRejected");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }

    [Authorize]
    [HttpDelete("{matchId:int}/participants/{participantId:int}")]
    public async Task<ActionResult<OpenMatchDetailResponse>> RemoveParticipant(
        int matchId,
        int participantId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách người chơi đang được cập nhật." });
        var match = await MatchPhase8Query().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (HasStarted(match))
            return Conflict(new { message = "Chủ trận không được xóa người sau khi trận bắt đầu." });
        if (HasSettledPayment(match))
            return Conflict(new { message = "Đã có giao dịch được thanh toán; cần xử lý hoàn tiền trước khi thay đổi đội hình." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.ParticipantId == participantId);
        if (participant is null || participant.IsHost || participant.Status != "Accepted")
            return Conflict(new { message = "Không thể xóa thành viên này." });
        participant.Status = "Removed";
        participant.RespondedAt = DateTime.UtcNow;
        ResetRosterToWaiting(match, "Chủ trận xóa thành viên");
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "ParticipantRemoved");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/cancel")]
    public async Task<ActionResult<OpenMatchDetailResponse>> CancelOpenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Trận đang được cập nhật." });
        var match = await MatchPhase8Query().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (match.Status == "Completed")
            return Conflict(new { message = "Không thể hủy trận đã hoàn thành." });
        if (match.Status == "Cancelled")
            return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));

        var booking = MatchBooking(match);
        match.Status = "Cancelled";
        match.CancelledAt = DateTime.UtcNow;
        var oldBookingStatus = booking.Status;
        booking.Status = "Cancelled";
        booking.HoldExpiresAt = null;
        booking.StatusHistories.Add(NewMatchBookingHistory(oldBookingStatus, "Cancelled", "Chủ trận hủy trận", CurrentUserIdPhase8()));
        foreach (var payment in booking.Payments.Where(item => item.Status is not "Cancelled" and not "Refunded"))
        {
            var previous = payment.Status;
            payment.Status = payment.Status == "Paid" ? "RefundPending" : "Cancelled";
            payment.StatusHistories.Add(NewMatchPaymentHistory(previous, payment.Status, "MatchCancelled", "Chủ trận hủy trận", CurrentUserIdPhase8()));
        }
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, "Cancelled", "Deleted"));
        _matchRealtime.Publish(matchId, "Cancelled");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/reopen")]
    public async Task<ActionResult<OpenMatchDetailResponse>> ReopenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Trận đang được cập nhật." });
        var match = await MatchPhase8Query().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (match.Status != "Cancelled")
            return Conflict(new { message = "Chỉ có thể mở lại trận đã hủy." });
        if (match.MatchTime <= DateTime.Now)
            return Conflict(new { message = "Không thể mở lại trận có thời gian bắt đầu đã qua." });

        var booking = MatchBooking(match);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"player-schedule:{hostPlayerId.Value}", cancellationToken))
            return Conflict(new { message = "Lịch của bạn đang được cập nhật. Vui lòng thử lại." });
        if (await _playerScheduleConflict.HasConflictAsync(
                hostPlayerId.Value,
                booking.StartTime,
                booking.EndTime,
                excludedBookingId: booking.BookingId,
                excludedMatchId: match.MatchId,
                cancellationToken: cancellationToken))
            return Conflict(new { message = "Bạn đã có lịch đặt sân hoặc ghép trận trùng với thời gian của trận này." });

        if (booking.Payments.Any(item => item.Status is "Paid" or "RefundPending"))
            return Conflict(new { message = "Cần hoàn tất hoàn tiền trước khi mở lại trận." });
        var overlap = await _db.Bookings.AnyAsync(item =>
            item.BookingId != booking.BookingId
            && item.CourtId == booking.CourtId
            && !MatchInactiveBookingStatuses.Contains(item.Status)
            && (item.Status != "Holding" || item.HoldExpiresAt > DateTime.UtcNow)
            && item.StartTime < booking.EndTime
            && item.EndTime > booking.StartTime,
            cancellationToken);
        if (overlap) return Conflict(new { message = "Khung giờ của trận đã được booking khác sử dụng." });

        foreach (var participant in match.MatchParticipants.Where(item => !item.IsHost))
        {
            participant.Status = "Removed";
            participant.RespondedAt = DateTime.UtcNow;
        }
        _db.Payments.RemoveRange(booking.Payments);
        match.Status = "Waiting";
        match.CancelledAt = null;
        var oldBookingStatus = booking.Status;
        booking.Status = "MatchWaiting";
        booking.HoldExpiresAt = DateTime.UtcNow.AddMinutes(MatchMatchingMinutes());
        booking.StatusHistories.Add(NewMatchBookingHistory(oldBookingStatus, "MatchWaiting", "Chủ trận mở lại trận", CurrentUserIdPhase8()));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, "MatchWaiting", "Created"));
        _matchRealtime.Publish(matchId, "Reopened");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/complete")]
    public async Task<ActionResult<OpenMatchDetailResponse>> CompleteOpenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });
        var match = await MatchPhase8Query().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận." });
        if (match.HostPlayerId != playerId) return Forbid();
        if (match.Status != "Confirmed")
            return Conflict(new { message = "Chỉ có thể hoàn thành trận đã xác nhận." });
        var booking = MatchBooking(match);
        if (booking.EndTime > DateTime.Now)
            return Conflict(new { message = "Trận chưa kết thúc." });

        match.Status = "Completed";
        var oldBookingStatus = booking.Status;
        booking.Status = "Completed";
        booking.StatusHistories.Add(NewMatchBookingHistory(oldBookingStatus, "Completed", "Chủ trận xác nhận hoàn thành", CurrentUserIdPhase8()));
        await _db.SaveChangesAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "Completed");
        return Ok(await LoadOpenMatchResponseAsync(matchId, playerId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/reviews/{revieweePlayerId:int}")]
    public async Task<ActionResult<MatchPlayerReviewResponse>> ReviewMatchPlayer(
        int matchId,
        int revieweePlayerId,
        CreateMatchPlayerReviewRequest request,
        CancellationToken cancellationToken)
    {
        var reviewer = await CurrentPlayerAsync(cancellationToken);
        if (reviewer is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });
        if (reviewer.PlayerId == revieweePlayerId)
            return BadRequest(new { message = "Bạn không thể tự đánh giá chính mình." });

        var match = await _db.Matches
            .Include(item => item.MatchParticipants).ThenInclude(item => item.Player).ThenInclude(item => item.User)
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận." });
        if (match.Status != "Completed")
            return Conflict(new { message = "Chỉ được đánh giá người chơi sau khi trận hoàn thành." });
        if (!match.MatchParticipants.Any(item => item.PlayerId == reviewer.PlayerId && item.Status == "Accepted")
            || !match.MatchParticipants.Any(item => item.PlayerId == revieweePlayerId && item.Status == "Accepted"))
            return Forbid();
        if (await _db.MatchPlayerReviews.AnyAsync(item =>
            item.MatchId == matchId
            && item.ReviewerPlayerId == reviewer.PlayerId
            && item.RevieweePlayerId == revieweePlayerId,
            cancellationToken))
            return Conflict(new { message = "Bạn đã đánh giá người chơi này trong trận." });

        var review = new MatchPlayerReview
        {
            MatchId = matchId,
            ReviewerPlayerId = reviewer.PlayerId,
            RevieweePlayerId = revieweePlayerId,
            Score = request.Score,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.MatchPlayerReviews.Add(review);
        await _db.SaveChangesAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "PlayerReviewed");
        var reviewee = match.MatchParticipants.Single(item => item.PlayerId == revieweePlayerId).Player;
        return Ok(new MatchPlayerReviewResponse
        {
            MatchPlayerReviewId = review.MatchPlayerReviewId,
            MatchId = matchId,
            ReviewerPlayerId = reviewer.PlayerId,
            ReviewerName = reviewer.User.Username,
            RevieweePlayerId = revieweePlayerId,
            RevieweeName = reviewee.User.Username,
            Score = review.Score,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt
        });
    }

    [Authorize]
    [HttpGet("{matchId:int}/reviews")]
    public async Task<ActionResult<List<MatchPlayerReviewResponse>>> GetMatchPlayerReviews(
        int matchId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });
        var isParticipant = await _db.MatchParticipants.AnyAsync(item =>
            item.MatchId == matchId && item.PlayerId == playerId && item.Status == "Accepted", cancellationToken);
        if (!isParticipant) return Forbid();

        var reviews = await _db.MatchPlayerReviews.AsNoTracking()
            .Where(item => item.MatchId == matchId)
            .Include(item => item.ReviewerPlayer).ThenInclude(item => item.User)
            .Include(item => item.RevieweePlayer).ThenInclude(item => item.User)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new MatchPlayerReviewResponse
            {
                MatchPlayerReviewId = item.MatchPlayerReviewId,
                MatchId = item.MatchId,
                ReviewerPlayerId = item.ReviewerPlayerId,
                ReviewerName = item.ReviewerPlayer.User.Username,
                RevieweePlayerId = item.RevieweePlayerId,
                RevieweeName = item.RevieweePlayer.User.Username,
                Score = item.Score,
                Comment = item.Comment,
                CreatedAt = item.CreatedAt
            })
            .ToListAsync(cancellationToken);
        return Ok(reviews);
    }

    private IQueryable<Match> MatchPhase8Query(bool asNoTracking = false)
    {
        IQueryable<Match> query = _db.Matches
            .AsSplitQuery()
            .Include(item => item.HostPlayer).ThenInclude(item => item!.User)
            .Include(item => item.MatchParticipants).ThenInclude(item => item.Player).ThenInclude(item => item.User)
            .Include(item => item.Bookings).ThenInclude(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.BookingRules)
            .Include(item => item.Bookings).ThenInclude(item => item.Payments).ThenInclude(item => item.StatusHistories);
        return asNoTracking ? query.AsNoTracking() : query;
    }

    private async Task<OpenMatchDetailResponse?> LoadOpenMatchResponseAsync(
        int matchId,
        int? currentPlayerId,
        CancellationToken cancellationToken)
    {
        await RepairLegacyMatchPricingAsync(matchId, cancellationToken);
        var match = await MatchPhase8Query(asNoTracking: true)
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null || match.Bookings.Count == 0) return null;
        var result = new OpenMatchDetailResponse();
        CopySearchResponse(MapSearchResponse(match, currentPlayerId), result);
        var booking = MatchBooking(match);
        var myPayment = currentPlayerId.HasValue
            ? booking.Payments.Where(item => item.PayerId == currentPlayerId.Value).OrderByDescending(item => item.PaymentId).FirstOrDefault()
            : null;
        var isAcceptedParticipant = currentPlayerId.HasValue
            && match.MatchParticipants.Any(item =>
                item.PlayerId == currentPlayerId.Value && item.Status == "Accepted");
        result.BookingId = booking.BookingId;
        result.MyPlayerId = currentPlayerId;
        result.CheckInCode = isAcceptedParticipant
            && (booking.Status == "Confirmed" || booking.Status == "Completed")
            ? booking.BookingCode
            : null;
        result.PaymentDeadline = AsUtcPhase8(booking.HoldExpiresAt);
        result.MyPaymentId = myPayment?.PaymentId;
        result.MyQrImageUrl = myPayment?.QrImageUrl;
        result.MyTransferContent = myPayment?.TransferContent;
        result.Participants = match.MatchParticipants
            .OrderByDescending(item => item.IsHost)
            .ThenBy(item => item.RequestedAt)
            .Select(item => new MatchParticipantResponse
            {
                ParticipantId = item.ParticipantId,
                PlayerId = item.PlayerId,
                PlayerName = item.Player.User.Username,
                AvatarUrl = item.Player.User.ProfileImageUrl,
                SkillLevel = item.Player.SkillLevel,
                Status = item.Status,
                IsHost = item.IsHost,
                RequestedAt = AsUtcPhase8(item.RequestedAt),
                RespondedAt = AsUtcPhase8(item.RespondedAt),
                PaymentStatus = booking.Payments
                    .Where(payment => payment.PayerId == item.PlayerId)
                    .OrderByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Status)
                    .FirstOrDefault()
            })
            .ToList();
        return result;
    }

    private static MatchSearchResponse MapSearchResponse(Match match, int? currentPlayerId)
    {
        var booking = MatchBooking(match);
        var acceptedCount = AcceptedParticipants(match).Count;
        var myParticipant = currentPlayerId.HasValue
            ? match.MatchParticipants.SingleOrDefault(item => item.PlayerId == currentPlayerId.Value)
            : null;
        var myPayment = currentPlayerId.HasValue
            ? booking.Payments.Where(item => item.PayerId == currentPlayerId.Value).OrderByDescending(item => item.PaymentId).FirstOrDefault()
            : null;
        return new MatchSearchResponse
        {
            MatchId = match.MatchId,
            HostPlayerId = match.HostPlayerId ?? 0,
            HostName = match.HostPlayer?.User.Username ?? "Chủ trận",
            HostAvatarUrl = match.HostPlayer?.User.ProfileImageUrl,
            MatchType = match.MatchType,
            MatchSkillLevel = match.MatchSkillLevel,
            Status = match.Status,
            Note = match.Note,
            RequiredPlayerCount = match.RequiredPlayerCount,
            AcceptedPlayerCount = acceptedCount,
            PendingRequestCount = match.MatchParticipants.Count(item => item.Status == "Pending"),
            AvailableSlotCount = Math.Max(match.RequiredPlayerCount - acceptedCount, 0),
            CourtId = booking.CourtId,
            CourtNumber = booking.Court.CourtNumber,
            VenueId = booking.Court.VenueId,
            VenueName = booking.Court.Venue.VenueName,
            Address = booking.Court.Venue.Address,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            TotalBookingAmount = EffectiveMatchTotal(booking),
            AmountPerPlayer = AmountPerPlayer(match, booking),
            IsHost = currentPlayerId.HasValue && match.HostPlayerId == currentPlayerId,
            MyParticipantStatus = myParticipant?.Status,
            MyPaymentStatus = myPayment?.Status
        };
    }

    private static void CopySearchResponse(MatchSearchResponse source, MatchSearchResponse target)
    {
        target.MatchId = source.MatchId;
        target.HostPlayerId = source.HostPlayerId;
        target.HostName = source.HostName;
        target.HostAvatarUrl = source.HostAvatarUrl;
        target.MatchType = source.MatchType;
        target.MatchSkillLevel = source.MatchSkillLevel;
        target.Status = source.Status;
        target.Note = source.Note;
        target.RequiredPlayerCount = source.RequiredPlayerCount;
        target.AcceptedPlayerCount = source.AcceptedPlayerCount;
        target.PendingRequestCount = source.PendingRequestCount;
        target.AvailableSlotCount = source.AvailableSlotCount;
        target.CourtId = source.CourtId;
        target.CourtNumber = source.CourtNumber;
        target.VenueId = source.VenueId;
        target.VenueName = source.VenueName;
        target.Address = source.Address;
        target.StartTime = source.StartTime;
        target.EndTime = source.EndTime;
        target.TotalBookingAmount = source.TotalBookingAmount;
        target.AmountPerPlayer = source.AmountPerPlayer;
        target.IsHost = source.IsHost;
        target.MyParticipantStatus = source.MyParticipantStatus;
        target.MyPaymentStatus = source.MyPaymentStatus;
    }

    private async Task PrepareSplitPaymentsAsync(Match match, CancellationToken cancellationToken)
    {
        var booking = MatchBooking(match);
        var accepted = AcceptedParticipants(match);
        if (accepted.Count != match.RequiredPlayerCount)
            throw new InvalidOperationException("Cannot prepare payments before the match is full.");

        var account = await _db.OwnerBankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == booking.Court.Venue.OwnerId && item.IsActive, cancellationToken);
        var amount = AmountPerPlayer(match, booking);
        foreach (var participant in accepted)
        {
            var transferContent = $"{booking.BookingCode}-P{participant.PlayerId}";
            var payment = new Payment
            {
                BookingId = booking.BookingId,
                PayerId = participant.PlayerId,
                Amount = amount,
                PaymentMethod = "BankTransfer",
                Status = "Pending",
                TransferCode = $"{booking.BookingCode?.Replace("-", string.Empty)}P{participant.PlayerId}",
                TransferContent = transferContent,
                BankCode = account?.BankCode,
                BankName = account?.BankName,
                BankAccountNumber = account?.AccountNumber,
                BankAccountName = account?.AccountHolderName,
                QrImageUrl = account is null ? null : BuildMatchVietQrUrl(account, amount, transferContent)
            };
            payment.StatusHistories.Add(NewMatchPaymentHistory(
                null, "Pending", "MatchSplitCreated", "Tạo phần thanh toán của người chơi", CurrentUserIdPhase8()));
            booking.Payments.Add(payment);
        }

        var previousBookingStatus = booking.Status;
        var paymentMinutes = Math.Clamp(_configuration.GetValue("Match:PaymentMinutes", 5), 1, 1440);
        booking.Status = "Holding";
        booking.HoldExpiresAt = DateTime.UtcNow.AddMinutes(paymentMinutes);
        booking.StatusHistories.Add(NewMatchBookingHistory(
            previousBookingStatus, "Holding", "Trận đủ người, bắt đầu chờ thanh toán", CurrentUserIdPhase8()));
        match.Status = "PaymentPending";
    }

    private void ResetRosterToWaiting(Match match, string reason)
    {
        var booking = MatchBooking(match);
        if (HasSettledPayment(match))
            throw new InvalidOperationException("Paid payments must be refunded before resetting the roster.");
        _db.Payments.RemoveRange(booking.Payments);
        var previousBookingStatus = booking.Status;
        booking.Status = "MatchWaiting";
        booking.HoldExpiresAt = DateTime.UtcNow.AddMinutes(MatchMatchingMinutes());
        booking.StatusHistories.Add(NewMatchBookingHistory(
            previousBookingStatus, "MatchWaiting", reason, CurrentUserIdPhase8()));
        match.Status = "Waiting";
    }

    private static Booking MatchBooking(Match match) =>
        match.Bookings.OrderBy(item => item.CreatedAt).First();

    private static List<MatchParticipant> AcceptedParticipants(Match match) =>
        match.MatchParticipants.Where(item => item.Status == "Accepted").ToList();

    private static bool HasSettledPayment(Match match) =>
        MatchBooking(match).Payments.Any(item => item.Status is "Paid" or "RefundPending" or "Refunded");

    private static bool HasStarted(Match match) =>
        MatchBooking(match).StartTime <= DateTime.Now || match.Status == "Completed";

    private async Task RepairLegacyMatchPricingAsync(int matchId, CancellationToken cancellationToken)
    {
        var match = await MatchPhase8Query()
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null || match.Bookings.Count == 0) return;

        var booking = MatchBooking(match);
        var hourlyPrice = EffectiveMatchHourlyPrice(booking);
        var totalAmount = EffectiveMatchTotal(booking);
        if (hourlyPrice <= 0 || totalAmount <= 0) return;

        var changed = false;
        if (booking.HourlyPriceSnapshot <= 0)
        {
            booking.HourlyPriceSnapshot = hourlyPrice;
            changed = true;
        }
        if (booking.CourtAmount <= 0)
        {
            booking.CourtAmount = totalAmount;
            changed = true;
        }
        if (booking.TotalAmount <= 0)
        {
            booking.TotalAmount = totalAmount;
            changed = true;
        }

        var amountPerPlayer = match.RequiredPlayerCount <= 0
            ? 0
            : Math.Ceiling(totalAmount / match.RequiredPlayerCount);
        foreach (var payment in booking.Payments.Where(item =>
                     item.Amount <= 0 && item.Status is "Pending" or "WaitingForConfirmation"))
        {
            payment.Amount = amountPerPlayer;
            if (!string.IsNullOrWhiteSpace(payment.BankCode)
                && !string.IsNullOrWhiteSpace(payment.BankAccountNumber)
                && !string.IsNullOrWhiteSpace(payment.TransferContent))
            {
                payment.QrImageUrl = BuildMatchVietQrUrl(
                    payment.BankCode,
                    payment.BankAccountNumber,
                    payment.BankAccountName ?? string.Empty,
                    amountPerPlayer,
                    payment.TransferContent);
            }
            changed = true;
        }

        if (changed) await _db.SaveChangesAsync(cancellationToken);
    }

    private static double AmountPerPlayer(Match match, Booking booking) =>
        match.RequiredPlayerCount <= 0 ? 0 : Math.Ceiling(EffectiveMatchTotal(booking) / match.RequiredPlayerCount);

    private static double EffectiveMatchTotal(Booking booking)
    {
        if (booking.TotalAmount > 0) return booking.TotalAmount;
        var durationHours = Math.Max(0, (booking.EndTime - booking.StartTime).TotalHours);
        return Math.Round(EffectiveMatchHourlyPrice(booking) * durationHours, 0, MidpointRounding.AwayFromZero);
    }

    private static double EffectiveMatchHourlyPrice(Booking booking)
    {
        if (booking.HourlyPriceSnapshot > 0) return booking.HourlyPriceSnapshot;
        if (booking.Court.HourlyPrice > 0) return booking.Court.HourlyPrice;
        return MatchVenueBasePrice(booking.Court.Venue);
    }

    private static double MatchVenueBasePrice(Venue venue) =>
        double.TryParse(
            venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private int MatchMatchingMinutes() =>
        Math.Clamp(_configuration.GetValue("Match:MatchingMinutes", 15), 1, 120);

    private static string? NormalizeMatchType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        return normalized switch
        {
            "1vs1" or "1v1" => "1vs1",
            "2vs2" or "2v2" => "2vs2",
            _ => null
        };
    }

    private async Task<Player?> CurrentPlayerAsync(CancellationToken cancellationToken)
    {
        var userId = CurrentUserIdPhase8();
        return userId is null
            ? null
            : await _db.Players.Include(item => item.User)
                .SingleOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
    }

    private async Task<int?> CurrentPlayerIdAsync(CancellationToken cancellationToken)
    {
        var userId = CurrentUserIdPhase8();
        return userId is null
            ? null
            : await _db.Players.Where(item => item.UserId == userId.Value)
                .Select(item => (int?)item.PlayerId)
                .SingleOrDefaultAsync(cancellationToken);
    }

    private int? CurrentUserIdPhase8() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static BookingStatusHistory NewMatchBookingHistory(
        string? from,
        string to,
        string reason,
        int? actorUserId) => new()
    {
        FromStatus = from,
        ToStatus = to,
        Reason = reason,
        ActorUserId = actorUserId,
        ChangedAt = DateTime.UtcNow
    };

    private static PaymentStatusHistory NewMatchPaymentHistory(
        string? from,
        string to,
        string action,
        string? reason,
        int? actorUserId) => new()
    {
        FromStatus = from,
        ToStatus = to,
        Action = action,
        Reason = reason,
        ActorUserId = actorUserId,
        CreatedAt = DateTime.UtcNow
    };

    private static string BuildMatchVietQrUrl(OwnerBankAccount account, double amount, string content)
        => BuildMatchVietQrUrl(account.BankCode, account.AccountNumber, account.AccountHolderName, amount, content);

    private static string BuildMatchVietQrUrl(
        string bankCode,
        string accountNumber,
        string accountName,
        double amount,
        string content)
    {
        var query = $"amount={Math.Round(amount):0}&addInfo={Uri.EscapeDataString(content)}&accountName={Uri.EscapeDataString(accountName)}";
        return $"https://img.vietqr.io/image/{Uri.EscapeDataString(bankCode)}-{Uri.EscapeDataString(accountNumber)}-compact2.png?{query}";
    }

    private static DateTime AsUtcPhase8(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtcPhase8(DateTime? value) => value.HasValue ? AsUtcPhase8(value.Value) : null;
}
