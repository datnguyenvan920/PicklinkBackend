using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Matches;

namespace PicklinkBackend.Controllers;

public partial class MatchController
{
    [Authorize]
    [HttpPost("open")]
    public async Task<ActionResult<OpenMatchDetailResponse>> CreateOpenMatch(
        CreateOpenMatchRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.CreateOpenMatch(request, cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("venues")]
    public async Task<ActionResult<List<MatchPreferredVenueResponse>>> SearchPreferredVenues(
        string? province,
        string? ward,
        double radiusKm = 5,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.SearchPreferredVenues(province, ward, radiusKm, latitude, longitude, cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("open")]
    public async Task<ActionResult<PaginatedResponse<MatchSearchResponse>>> GetOpenMatches(
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
        SetCurrentUser();
        return ToActionResult(await _matchService.GetOpenMatches(owner, matchType, skillLevel, from, to, province, ward, page, pageSize, cancellationToken));
    }

    [Authorize]
    [HttpGet("mine")]
    public async Task<ActionResult<PaginatedResponse<MatchSearchResponse>>> GetMyOpenMatches(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.GetMyOpenMatches(page, pageSize, cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("{matchId:int}")]
    public async Task<ActionResult<OpenMatchDetailResponse>> GetOpenMatchDetail(
        int matchId,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.GetOpenMatchDetail(matchId, cancellationToken));
    }

    [Authorize]
    [HttpPut("{matchId:int}")]
    public async Task<ActionResult<OpenMatchDetailResponse>> UpdateOpenMatchInvitation(
        int matchId,
        UpdateOpenMatchInvitationRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.UpdateOpenMatchInvitation(matchId, request, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/join")]
    public async Task<ActionResult<OpenMatchDetailResponse>> JoinOpenMatch(int matchId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.JoinOpenMatch(matchId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/leave")]
    public async Task<ActionResult<OpenMatchDetailResponse>> LeaveOpenMatch(int matchId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.LeaveOpenMatch(matchId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/participants/{participantId:int}/accept")]
    public async Task<ActionResult<OpenMatchDetailResponse>> AcceptParticipant(int matchId, int participantId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.AcceptParticipant(matchId, participantId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/participants/{participantId:int}/reject")]
    public async Task<ActionResult<OpenMatchDetailResponse>> RejectParticipant(int matchId, int participantId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.RejectParticipant(matchId, participantId, cancellationToken));
    }

    [Authorize]
    [HttpDelete("{matchId:int}/participants/{participantId:int}")]
    public async Task<ActionResult<OpenMatchDetailResponse>> RemoveParticipant(int matchId, int participantId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.RemoveParticipant(matchId, participantId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/ready")]
    public async Task<ActionResult<OpenMatchDetailResponse>> MarkReadyToBook(int matchId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.MarkReadyToBook(matchId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/booking")]
    public async Task<ActionResult<OpenMatchDetailResponse>> CreateMatchBooking(int matchId, CreateMatchBookingRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.CreateMatchBooking(matchId, request, cancellationToken));
    }

    [Authorize]
    [HttpDelete("{matchId:int}/booking")]
    public async Task<ActionResult<OpenMatchDetailResponse>> CancelPendingMatchBooking(int matchId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.CancelPendingMatchBooking(matchId, cancellationToken));
    }

    [Authorize]
    [HttpGet("{matchId:int}/slot-options")]
    public async Task<ActionResult<List<MatchSlotOptionResponse>>> GetMatchSlotOptions(int matchId, int venueId, DateOnly date, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.GetMatchSlotOptions(matchId, venueId, date, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/slot-votes")]
    public async Task<ActionResult<List<MatchSlotOptionResponse>>> VoteMatchSlot(int matchId, MatchSlotVoteRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.VoteMatchSlot(matchId, request, cancellationToken));
    }

    [Authorize]
    [HttpDelete("{matchId:int}/slot-votes")]
    public async Task<ActionResult<List<MatchSlotOptionResponse>>> UnvoteMatchSlot(int matchId, MatchSlotVoteRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.UnvoteMatchSlot(matchId, request, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/check-in-groups/{bookingCheckInGroupId:int}/unavailable")]
    public async Task<ActionResult<OpenMatchDetailResponse>> ReportSlotUnavailable(int matchId, int bookingCheckInGroupId, [FromBody] ReportMatchSlotAbsenceRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.ReportSlotUnavailable(matchId, bookingCheckInGroupId, request, cancellationToken));
    }

    [Authorize]
    [HttpDelete("{matchId:int}/slot-absences/{matchSlotAbsenceId:int}")]
    public async Task<ActionResult<OpenMatchDetailResponse>> CancelSlotUnavailable(int matchId, int matchSlotAbsenceId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.CancelSlotUnavailable(matchId, matchSlotAbsenceId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/slot-absences/{matchSlotAbsenceId:int}/replacement-requests")]
    public async Task<ActionResult<OpenMatchDetailResponse>> ApplyForSlotReplacement(int matchId, int matchSlotAbsenceId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.ApplyForSlotReplacement(matchId, matchSlotAbsenceId, cancellationToken));
    }

    [Authorize]
    [HttpDelete("{matchId:int}/slot-absences/{matchSlotAbsenceId:int}/replacement-requests/mine")]
    public async Task<ActionResult<OpenMatchDetailResponse>> WithdrawSlotReplacement(int matchId, int matchSlotAbsenceId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.WithdrawSlotReplacement(matchId, matchSlotAbsenceId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/slot-absences/{matchSlotAbsenceId:int}/replacement-requests/{replacementRequestId:int}/accept")]
    public async Task<ActionResult<OpenMatchDetailResponse>> AcceptSlotReplacement(int matchId, int matchSlotAbsenceId, int replacementRequestId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.AcceptSlotReplacement(matchId, matchSlotAbsenceId, replacementRequestId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/slot-absences/{matchSlotAbsenceId:int}/replacement-requests/{replacementRequestId:int}/reject")]
    public async Task<ActionResult<OpenMatchDetailResponse>> RejectSlotReplacement(int matchId, int matchSlotAbsenceId, int replacementRequestId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.RejectSlotReplacement(matchId, matchSlotAbsenceId, replacementRequestId, cancellationToken));
    }

    [Authorize]
    [HttpDelete("{matchId:int}/slot-absences/{matchSlotAbsenceId:int}/replacement-requests/{replacementRequestId:int}")]
    public async Task<ActionResult<OpenMatchDetailResponse>> RemoveSlotReplacement(int matchId, int matchSlotAbsenceId, int replacementRequestId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.RemoveSlotReplacement(matchId, matchSlotAbsenceId, replacementRequestId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/complete")]
    public async Task<ActionResult<OpenMatchDetailResponse>> CompleteOpenMatch(int matchId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.CompleteOpenMatch(matchId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/reviews/{revieweePlayerId:int}")]
    public async Task<ActionResult<MatchPlayerReviewResponse>> ReviewMatchPlayer(int matchId, int revieweePlayerId, CreateMatchPlayerReviewRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.ReviewMatchPlayer(matchId, revieweePlayerId, request, cancellationToken));
    }

    [Authorize]
    [HttpGet("{matchId:int}/reviews")]
    public async Task<ActionResult<List<MatchPlayerReviewResponse>>> GetMatchPlayerReviews(int matchId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.GetMatchPlayerReviews(matchId, cancellationToken));
    }
}