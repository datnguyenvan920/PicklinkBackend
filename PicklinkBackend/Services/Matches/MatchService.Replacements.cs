using System.Data;
using PicklinkBackend.Services.Bookings;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches;

public partial class MatchService
{
    public async Task<ServiceResult<OpenMatchDetailResponse>> ReportSlotUnavailable(
        int matchId,
        int bookingCheckInGroupId,
        ReportMatchSlotAbsenceRequest request,
        CancellationToken cancellationToken)
    {
        var player = await CurrentPlayerAsync(cancellationToken);
        if (player is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách theo buổi đang được cập nhật. Vui lòng thử lại." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        if (match.Status is not ("BookingPending" or "Booked"))
            return Conflict(new { message = "Chỉ có thể báo bận sau khi phòng đã tạo booking." });
        if (!ApprovedParticipants(match).Any(item => item.PlayerId == player.PlayerId)) return Forbid();

        var group = match.Bookings
            .Where(booking => !InactiveBookingStatuses.Contains(booking.Status))
            .SelectMany(booking => booking.CheckInGroups)
            .SingleOrDefault(item => item.BookingCheckInGroupId == bookingCheckInGroupId);
        if (group is null) return NotFound(new { message = "Không tìm thấy buổi chơi trong booking này." });
        if (group.StartTime <= VietnamTime.Now)
            return Conflict(new { message = "Buổi chơi đã bắt đầu nên không thể báo bận." });

        var absence = match.SlotAbsences.SingleOrDefault(item =>
            item.BookingCheckInGroupId == bookingCheckInGroupId
            && item.UnavailablePlayerId == player.PlayerId);
        if (absence?.Status == "Filled")
            return Conflict(new { message = "Buổi này đã có người thay thế nên không thể thay đổi báo bận." });

        var now = DateTime.UtcNow;
        if (absence is null)
        {
            absence = new MatchSlotAbsence
            {
                MatchId = matchId,
                BookingCheckInGroupId = bookingCheckInGroupId,
                UnavailablePlayerId = player.PlayerId,
                Status = "Open",
                Reason = NormalizeAbsenceReason(request.Reason),
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.MatchSlotAbsences.Add(absence);
        }
        else
        {
            absence.Status = "Open";
            absence.Reason = NormalizeAbsenceReason(request.Reason);
            absence.UpdatedAt = now;
            foreach (var replacementRequest in absence.ReplacementRequests.Where(item => item.Status == "Pending"))
            {
                replacementRequest.Status = "Rejected";
                replacementRequest.RespondedAt = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "SlotAbsenceReported");
        return Ok((await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken))!);
    }

    public async Task<ServiceResult<OpenMatchDetailResponse>> CancelSlotUnavailable(
        int matchId,
        int matchSlotAbsenceId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách theo buổi đang được cập nhật. Vui lòng thử lại." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        var absence = match.SlotAbsences.SingleOrDefault(item => item.MatchSlotAbsenceId == matchSlotAbsenceId);
        if (absence is null) return NotFound(new { message = "Không tìm thấy báo bận này." });
        if (absence.UnavailablePlayerId != playerId.Value) return Forbid();
        if (absence.ReplacementRequests.Any(item => item.Status == "Approved"))
            return Conflict(new { message = "Đã có người được duyệt thay thế cho buổi này." });

        var now = DateTime.UtcNow;
        absence.Status = "Cancelled";
        absence.UpdatedAt = now;
        foreach (var replacementRequest in absence.ReplacementRequests.Where(item => item.Status == "Pending"))
        {
            replacementRequest.Status = "Rejected";
            replacementRequest.RespondedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "SlotAbsenceCancelled");
        return Ok((await LoadOpenMatchResponseAsync(matchId, playerId, cancellationToken))!);
    }

    public async Task<ServiceResult<OpenMatchDetailResponse>> ApplyForSlotReplacement(
        int matchId,
        int matchSlotAbsenceId,
        CancellationToken cancellationToken)
    {
        var player = await CurrentPlayerAsync(cancellationToken);
        if (player is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken)
            || !await SqlServerBookingLock.AcquireAsync(_db, transaction, $"player-schedule:{player.PlayerId}", cancellationToken))
            return Conflict(new { message = "Lịch hoặc danh sách người chơi đang được cập nhật. Vui lòng thử lại." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        var absence = match.SlotAbsences.SingleOrDefault(item => item.MatchSlotAbsenceId == matchSlotAbsenceId);
        if (absence is null || absence.Status != "Open")
            return Conflict(new { message = "Vị trí thay thế này không còn mở." });
        if (absence.UnavailablePlayerId == player.PlayerId
            || match.MatchParticipants.Any(item => item.PlayerId == player.PlayerId
                && item.Status is "Invited" or "Pending" or "Approved" or "Accepted"))
            return Conflict(new { message = "Thành viên của phòng không thể đăng ký làm người thay thế." });
        if (player.SkillLevel < match.MinSkillLevel || player.SkillLevel > match.MaxSkillLevel)
            return Conflict(new { message = $"Trình độ của bạn chưa nằm trong khoảng {match.MinSkillLevel}–{match.MaxSkillLevel}." });

        var group = absence.BookingCheckInGroup;
        if (group.StartTime <= VietnamTime.Now)
            return Conflict(new { message = "Buổi chơi đã bắt đầu hoặc đã kết thúc." });
        if (await _playerScheduleConflict.HasConflictAsync(
            player.PlayerId,
            group.StartTime,
            group.EndTime,
            excludedMatchId: matchId,
            cancellationToken: cancellationToken))
            return Conflict(new { message = "Bạn đã có lịch trùng với buổi cần người thay thế." });

        var replacementRequest = absence.ReplacementRequests.SingleOrDefault(item => item.PlayerId == player.PlayerId);
        if (replacementRequest?.Status is "Pending" or "Approved")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Ok((await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken))!);
        }

        if (replacementRequest is null)
        {
            replacementRequest = new MatchSlotReplacementRequest
            {
                MatchSlotAbsenceId = matchSlotAbsenceId,
                PlayerId = player.PlayerId,
                Status = "Pending",
                RequestedAt = DateTime.UtcNow
            };
            _db.MatchSlotReplacementRequests.Add(replacementRequest);
        }
        else
        {
            replacementRequest.Status = "Pending";
            replacementRequest.RequestedAt = DateTime.UtcNow;
            replacementRequest.RespondedAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "SlotReplacementRequested");
        return Ok((await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken))!);
    }

    public async Task<ServiceResult<OpenMatchDetailResponse>> WithdrawSlotReplacement(
        int matchId,
        int matchSlotAbsenceId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách theo buổi đang được cập nhật. Vui lòng thử lại." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        var absence = match.SlotAbsences.SingleOrDefault(item => item.MatchSlotAbsenceId == matchSlotAbsenceId);
        if (absence is null) return NotFound(new { message = "Không tìm thấy vị trí cần thay thế." });
        var replacementRequest = absence.ReplacementRequests.SingleOrDefault(item => item.PlayerId == playerId.Value);
        if (replacementRequest is null) return NotFound(new { message = "Không tìm thấy đơn thay thế của bạn." });
        if (replacementRequest.Status is not ("Pending" or "Approved"))
            return Conflict(new { message = "Đơn thay thế này không còn có thể rút hoặc rời." });

        var wasApproved = replacementRequest.Status == "Approved";
        if (wasApproved && absence.BookingCheckInGroup.StartTime <= VietnamTime.Now)
            return Conflict(new { message = "Buổi chơi đã bắt đầu nên không thể rời nhóm thay thế." });

        if (wasApproved)
            await ReleaseApprovedSlotReplacementAsync(match, absence, replacementRequest, "Left", cancellationToken);
        else
        {
            replacementRequest.Status = "Withdrawn";
            replacementRequest.RespondedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, wasApproved ? "SlotReplacementLeft" : "SlotReplacementWithdrawn");
        return Ok((await LoadOpenMatchResponseAsync(matchId, playerId, cancellationToken))!);
    }

    public async Task<ServiceResult<OpenMatchDetailResponse>> RemoveSlotReplacement(
        int matchId,
        int matchSlotAbsenceId,
        int replacementRequestId,
        CancellationToken cancellationToken)
    {
        var reviewerPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (reviewerPlayerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách theo buổi đang được cập nhật. Vui lòng thử lại." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        if (!ApprovedParticipants(match).Any(item => item.PlayerId == reviewerPlayerId.Value)) return Forbid();
        var absence = match.SlotAbsences.SingleOrDefault(item => item.MatchSlotAbsenceId == matchSlotAbsenceId);
        if (absence is null) return NotFound(new { message = "Không tìm thấy vị trí cần thay thế." });
        var replacementRequest = absence.ReplacementRequests
            .SingleOrDefault(item => item.MatchSlotReplacementRequestId == replacementRequestId);
        if (replacementRequest is null) return NotFound(new { message = "Không tìm thấy người thay thế." });
        if (replacementRequest.Status != "Approved")
            return Conflict(new { message = "Chỉ có thể đưa người thay thế đã được duyệt khỏi nhóm." });
        if (absence.BookingCheckInGroup.StartTime <= VietnamTime.Now)
            return Conflict(new { message = "Buổi chơi đã bắt đầu nên không thể đưa người thay thế khỏi nhóm." });

        await ReleaseApprovedSlotReplacementAsync(match, absence, replacementRequest, "Removed", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "SlotReplacementRemoved");
        return Ok((await LoadOpenMatchResponseAsync(matchId, reviewerPlayerId, cancellationToken))!);
    }

    public Task<ServiceResult<OpenMatchDetailResponse>> AcceptSlotReplacement(
        int matchId,
        int matchSlotAbsenceId,
        int replacementRequestId,
        CancellationToken cancellationToken) =>
        ReviewSlotReplacement(matchId, matchSlotAbsenceId, replacementRequestId, true, cancellationToken);

    public Task<ServiceResult<OpenMatchDetailResponse>> RejectSlotReplacement(
        int matchId,
        int matchSlotAbsenceId,
        int replacementRequestId,
        CancellationToken cancellationToken) =>
        ReviewSlotReplacement(matchId, matchSlotAbsenceId, replacementRequestId, false, cancellationToken);

    private async Task<ServiceResult<OpenMatchDetailResponse>> ReviewSlotReplacement(
        int matchId,
        int matchSlotAbsenceId,
        int replacementRequestId,
        bool accept,
        CancellationToken cancellationToken)
    {
        var reviewerPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (reviewerPlayerId is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sách theo buổi đang được cập nhật. Vui lòng thử lại." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy phòng ghép trận." });
        if (!ApprovedParticipants(match).Any(item => item.PlayerId == reviewerPlayerId.Value)) return Forbid();
        var absence = match.SlotAbsences.SingleOrDefault(item => item.MatchSlotAbsenceId == matchSlotAbsenceId);
        if (absence is null) return NotFound(new { message = "Không tìm thấy vị trí cần thay thế." });
        var replacementRequest = absence.ReplacementRequests
            .SingleOrDefault(item => item.MatchSlotReplacementRequestId == replacementRequestId);
        if (replacementRequest is null) return NotFound(new { message = "Không tìm thấy đơn thay thế." });
        if (replacementRequest.Status != "Pending")
            return Conflict(new { message = "Đơn thay thế này đã được xử lý." });

        if (accept && (replacementRequest.Player.SkillLevel < match.MinSkillLevel
            || replacementRequest.Player.SkillLevel > match.MaxSkillLevel))
            return Conflict(new { message = "Trình độ của ứng viên không còn phù hợp với phòng." });
        if (accept && match.MatchParticipants.Any(item => item.PlayerId == replacementRequest.PlayerId && IsApproved(item)))
            return Conflict(new { message = "Ứng viên hiện đã là thành viên của phòng." });

        var now = DateTime.UtcNow;
        var hasOtherActiveReplacementSlot = false;
        if (!accept)
        {
            replacementRequest.Status = "Rejected";
            replacementRequest.RespondedAt = now;
        }
        else
        {
            if (absence.Status != "Open") return Conflict(new { message = "Vị trí thay thế này không còn mở." });
            if (absence.BookingCheckInGroup.StartTime <= VietnamTime.Now)
                return Conflict(new { message = "Buổi chơi đã bắt đầu hoặc đã kết thúc." });
            if (match.SlotAbsences.Any(otherAbsence =>
                otherAbsence.MatchSlotAbsenceId != absence.MatchSlotAbsenceId
                && otherAbsence.BookingCheckInGroupId == absence.BookingCheckInGroupId
                && otherAbsence.ReplacementRequests.Any(request =>
                    request.PlayerId == replacementRequest.PlayerId && request.Status == "Approved")))
                return Conflict(new { message = "Ứng viên đã thay một vị trí khác trong cùng buổi chơi." });

            if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"player-schedule:{replacementRequest.PlayerId}", cancellationToken))
                return Conflict(new { message = "Lịch của ứng viên đang được cập nhật. Vui lòng thử lại." });
            if (await _playerScheduleConflict.HasConflictAsync(
                replacementRequest.PlayerId,
                absence.BookingCheckInGroup.StartTime,
                absence.BookingCheckInGroup.EndTime,
                excludedMatchId: matchId,
                cancellationToken: cancellationToken))
                return Conflict(new { message = "Ứng viên đã có lịch trùng với buổi này." });

            hasOtherActiveReplacementSlot = match.SlotAbsences.Any(otherAbsence =>
                otherAbsence.ReplacementRequests.Any(request =>
                    request.MatchSlotReplacementRequestId != replacementRequestId
                    && request.PlayerId == replacementRequest.PlayerId
                    && request.Status == "Approved")
                && match.Bookings.Any(booking =>
                    booking.BookingId == otherAbsence.BookingCheckInGroup.BookingId
                    && (booking.Status == "Holding" || booking.Status == "Confirmed"))
                && otherAbsence.BookingCheckInGroup.EndTime.AddHours(2) > VietnamTime.Now);

            replacementRequest.Status = "Approved";
            replacementRequest.RespondedAt = now;
            absence.Status = "Filled";
            absence.UpdatedAt = now;
            foreach (var other in absence.ReplacementRequests.Where(item =>
                item.MatchSlotReplacementRequestId != replacementRequestId && item.Status == "Pending"))
            {
                other.Status = "Rejected";
                other.RespondedAt = now;
            }
        }
        if (accept)
        {
            await AddConversationParticipantAsync(
                match, replacementRequest.Player.UserId, cancellationToken,
                resetJoinedAt: !hasOtherActiveReplacementSlot);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, accept ? "SlotReplacementApproved" : "SlotReplacementRejected");
        return Ok((await LoadOpenMatchResponseAsync(matchId, reviewerPlayerId, cancellationToken))!);
    }

    private async Task ReleaseApprovedSlotReplacementAsync(
        Match match,
        MatchSlotAbsence absence,
        MatchSlotReplacementRequest replacementRequest,
        string releasedStatus,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        replacementRequest.Status = releasedStatus;
        replacementRequest.RespondedAt = now;
        absence.Status = "Open";
        absence.UpdatedAt = now;

        var hasOtherActiveReplacementSlot = match.SlotAbsences.Any(otherAbsence =>
            otherAbsence.ReplacementRequests.Any(request =>
                request.PlayerId == replacementRequest.PlayerId && request.Status == "Approved")
            && match.Bookings.Any(booking =>
                booking.BookingId == otherAbsence.BookingCheckInGroup.BookingId
                && (booking.Status == "Holding" || booking.Status == "Confirmed"))
            && otherAbsence.BookingCheckInGroup.EndTime.AddHours(2) > VietnamTime.Now);
        if (!hasOtherActiveReplacementSlot
            && !ApprovedParticipants(match).Any(item => item.PlayerId == replacementRequest.PlayerId))
        {
            await RemoveConversationParticipantAsync(
                match, replacementRequest.Player.UserId, cancellationToken);
        }
    }

    private static string? NormalizeAbsenceReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}
