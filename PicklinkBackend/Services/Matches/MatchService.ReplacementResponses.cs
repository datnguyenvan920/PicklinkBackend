using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Matches;

public partial class MatchService
{
    private async Task<List<MatchBookingCheckInResponse>> BuildVisibleBookingRoundsAsync(
        Match match,
        int? currentPlayerId,
        bool isApprovedParticipant,
        DateTime localNow,
        CancellationToken cancellationToken)
    {
        var isRoomParticipant = currentPlayerId.HasValue
            && match.MatchParticipants.Any(item => item.PlayerId == currentPlayerId.Value
                && item.Status is "Invited" or "Pending" or "Approved" or "Accepted");
        var isApprovedReplacement = currentPlayerId.HasValue
            && match.SlotAbsences.Any(absence =>
                absence.BookingCheckInGroup.EndTime.AddHours(2) > localNow
                && match.Bookings.Any(booking =>
                    booking.BookingId == absence.BookingCheckInGroup.BookingId
                    && (booking.Status == "Holding" || booking.Status == "Confirmed"))
                && absence.ReplacementRequests.Any(request =>
                request.PlayerId == currentPlayerId.Value && request.Status == "Approved"));
        var hasOpenReplacementSlot = match.SlotAbsences.Any(absence =>
            absence.Status == "Open" && absence.BookingCheckInGroup.StartTime > localNow);
        if (!isApprovedParticipant && !isApprovedReplacement && !hasOpenReplacementSlot) return [];

        var currentPlayerSkillLevel = currentPlayerId.HasValue
            ? await _db.Players.AsNoTracking()
                .Where(player => player.PlayerId == currentPlayerId.Value)
                .Select(player => (double?)player.SkillLevel)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        return match.Bookings
            .Where(booking => booking.Status is "Holding" or "Confirmed")
            .OrderBy(booking => booking.StartTime)
            .ThenBy(booking => booking.BookingId)
            .Select(booking => new MatchBookingCheckInResponse
            {
                BookingId = booking.BookingId,
                BookingStatus = booking.Status,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                CheckInGroups = booking.CheckInGroups
                    .OrderBy(group => group.StartTime)
                    .ThenBy(group => group.CourtId)
                    .Select(group => MapBookingRound(
                        match,
                        booking,
                        group,
                        currentPlayerId,
                        currentPlayerSkillLevel,
                        isRoomParticipant,
                        isApprovedParticipant,
                        localNow))
                    .ToList()
            })
            .ToList();
    }

    private static MatchBookingCheckInGroupResponse MapBookingRound(
        Match match,
        Booking booking,
        BookingCheckInGroup group,
        int? currentPlayerId,
        double? currentPlayerSkillLevel,
        bool isRoomParticipant,
        bool isApprovedParticipant,
        DateTime localNow)
    {
        var groupAbsences = match.SlotAbsences
            .Where(absence => absence.BookingCheckInGroupId == group.BookingCheckInGroupId
                && absence.Status != "Cancelled")
            .OrderBy(absence => absence.CreatedAt)
            .ToList();
        var playerPayment = booking.Payments
            .Where(payment => payment.PayerId == currentPlayerId && payment.Status == "Paid")
            .OrderByDescending(payment => payment.PaymentId)
            .FirstOrDefault();
        var isWindowOpen = booking.Status == "Confirmed"
            && localNow >= group.StartTime.AddMinutes(-30)
            && localNow <= group.EndTime;

        return new MatchBookingCheckInGroupResponse
        {
            BookingCheckInGroupId = group.BookingCheckInGroupId,
            CourtId = group.CourtId,
            CourtNumber = group.Court.CourtNumber,
            StartTime = group.StartTime,
            EndTime = group.EndTime,
            CheckInCode = isApprovedParticipant && isWindowOpen && group.CheckInStatus == "Ready"
                ? playerPayment?.TransferCode
                : null,
            CheckInStatus = group.CheckInStatus,
            IsCheckInWindowOpen = isApprovedParticipant && isWindowOpen,
            CanReportUnavailable = isApprovedParticipant
                && group.StartTime > localNow
                && !groupAbsences.Any(absence => absence.UnavailablePlayerId == currentPlayerId
                    && (absence.Status is "Open" or "Filled")),
            Absences = groupAbsences
                .Select(absence => MapSlotAbsence(
                    match,
                    group,
                    absence,
                    currentPlayerId,
                    currentPlayerSkillLevel,
                    isRoomParticipant,
                    isApprovedParticipant,
                    localNow))
                .ToList()
        };
    }

    private static MatchSlotAbsenceResponse MapSlotAbsence(
        Match match,
        BookingCheckInGroup group,
        MatchSlotAbsence absence,
        int? currentPlayerId,
        double? currentPlayerSkillLevel,
        bool isRoomParticipant,
        bool canReviewReplacements,
        DateTime localNow)
    {
        var myRequest = currentPlayerId.HasValue
            ? absence.ReplacementRequests.SingleOrDefault(request => request.PlayerId == currentPlayerId.Value)
            : null;
        return new MatchSlotAbsenceResponse
        {
            MatchSlotAbsenceId = absence.MatchSlotAbsenceId,
            UnavailablePlayerId = absence.UnavailablePlayerId,
            UnavailablePlayerName = absence.UnavailablePlayer.User.Username,
            UnavailablePlayerAvatarUrl = absence.UnavailablePlayer.User.ProfileImageUrl,
            Status = absence.Status,
            Reason = absence.Reason,
            CreatedAt = AsUtc(absence.CreatedAt),
            CanCancel = absence.Status == "Open"
                && absence.UnavailablePlayerId == currentPlayerId
                && !absence.ReplacementRequests.Any(request => request.Status == "Approved"),
            CanApply = absence.Status == "Open"
                && group.StartTime > localNow
                && currentPlayerId.HasValue
                && !isRoomParticipant
                && absence.UnavailablePlayerId != currentPlayerId
                && myRequest?.Status is not ("Pending" or "Approved")
                && currentPlayerSkillLevel >= match.MinSkillLevel
                && currentPlayerSkillLevel <= match.MaxSkillLevel,
            MyRequestStatus = myRequest?.Status,
            ReplacementRequests = absence.ReplacementRequests
                .Where(request => canReviewReplacements || request.Status == "Approved" || request.PlayerId == currentPlayerId)
                .OrderBy(request => request.RequestedAt)
                .Select(request => new MatchSlotReplacementRequestResponse
                {
                    MatchSlotReplacementRequestId = request.MatchSlotReplacementRequestId,
                    PlayerId = request.PlayerId,
                    PlayerName = request.Player.User.Username,
                    AvatarUrl = request.Player.User.ProfileImageUrl,
                    SkillLevel = request.Player.SkillLevel,
                    Status = request.Status,
                    RequestedAt = AsUtc(request.RequestedAt),
                    RespondedAt = AsUtc(request.RespondedAt),
                    IsMine = request.PlayerId == currentPlayerId
                })
                .ToList()
        };
    }
}
