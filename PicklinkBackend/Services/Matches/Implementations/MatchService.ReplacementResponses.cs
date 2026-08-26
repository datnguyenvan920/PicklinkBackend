using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;

namespace PicklinkBackend.Services.Matches.Implementations;

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

        double? currentPlayerSkillLevel = null;
        if (currentPlayerId.HasValue)
        {
            currentPlayerSkillLevel = match.MatchParticipants
                .Where(participant => participant.PlayerId == currentPlayerId.Value)
                .Select(participant => (double?)participant.Player.SkillLevel)
                .FirstOrDefault()
                ?? match.SlotAbsences
                    .SelectMany(absence => absence.ReplacementRequests)
                    .Where(request => request.PlayerId == currentPlayerId.Value)
                    .Select(request => (double?)request.Player.SkillLevel)
                    .FirstOrDefault();

            if (!currentPlayerSkillLevel.HasValue)
            {
                currentPlayerSkillLevel = await _matchRepository.Players.AsNoTracking()
                    .Where(player => player.PlayerId == currentPlayerId.Value)
                    .Select(player => (double?)player.SkillLevel)
                    .SingleOrDefaultAsync(cancellationToken);
            }
        }
        return match.Bookings
            .Where(booking => booking.Status is "Holding" or "Confirmed" or "Completed")
            .OrderBy(booking => booking.StartTime)
            .ThenBy(booking => booking.BookingId)
            .Select(booking => new MatchBookingCheckInResponse
            {
                BookingId = booking.BookingId,
                BookingStatus = booking.Status,
                VenueId = booking.Court.VenueId,
                VenueName = booking.Court.Venue.VenueName,
                VenueAddress = booking.Court.Venue.Address,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                TotalBookingAmount = booking.TotalAmount,
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
        // A roster member's `isApprovedParticipant` flag doesn't cover someone who joined this specific
        // slot as an approved replacement (MatchSlotReplacementRequest), since they never get added to
        // MatchParticipants — without this, their check-in code/window stayed gated off for the whole
        // slot they were approved to play.
        var isApprovedReplacementForGroup = currentPlayerId.HasValue
            && groupAbsences.Any(absence => absence.ReplacementRequests.Any(request =>
                request.PlayerId == currentPlayerId.Value && request.Status == "Approved"));
        var isAuthorizedForGroup = isApprovedParticipant || isApprovedReplacementForGroup;
        // A replacement never gets their own Payment row for this booking — the slot's cost is already
        // covered by the player they're replacing — so they check in with that player's personal code
        // instead of one of their own.
        var payingPlayerId = isApprovedReplacementForGroup && !isApprovedParticipant
            ? groupAbsences
                .Where(absence => absence.ReplacementRequests.Any(request =>
                    request.PlayerId == currentPlayerId && request.Status == "Approved"))
                .Select(absence => (int?)absence.UnavailablePlayerId)
                .FirstOrDefault()
            : currentPlayerId;
        var playerPayment = booking.Payments
            .Where(payment => payment.PayerId == payingPlayerId && payment.Status == "Paid")
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
            CheckInCode = isAuthorizedForGroup && isWindowOpen && group.CheckInStatus == "Ready"
                ? CheckInCode.Compact(playerPayment?.TransferCode)
                : null,
            CheckInStatus = group.CheckInStatus,
            IsCheckInWindowOpen = isAuthorizedForGroup && isWindowOpen,
            CanReportUnavailable = isAuthorizedForGroup
                && group.StartTime > localNow
                && !match.MatchCheckIns.Any(checkIn => checkIn.PlayerId == currentPlayerId
                    && checkIn.BookingCheckInGroupId == group.BookingCheckInGroupId
                    && checkIn.Status == "Present")
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
                .Where(request => absence.UnavailablePlayerId == currentPlayerId || request.Status == "Approved" || request.PlayerId == currentPlayerId)
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

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
