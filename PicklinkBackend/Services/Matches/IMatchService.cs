using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches;

public interface IMatchService
{
    void SetCurrentUserId(int? userId);
    Task<ServiceResult<LobbyMeResponse>> LobbyMe();
    Task<ServiceResult<OpenMatchDetailResponse>> CreateMatch(CreateMatchRequest createMatch, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<MyMatchResponse>>> MyMatches();
    Task<ServiceResult<MatchVotingStatusResponse>> GetVotingStatus(int matchId);
    Task<ServiceResult<MatchVotingStatusResponse>> Vote(int matchId, CastVoteRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<MatchDetailResponse>> GetDetail(int matchId);
    Task<ServiceResult> GetMessages(int matchId, CancellationToken cancellationToken = default);
    Task<ServiceResult> SendMessage(int matchId, SendMatchMessageRequest request, CancellationToken cancellationToken = default);

    // Open Match methods
    Task<ServiceResult<OpenMatchDetailResponse>> CreateOpenMatch(CreateOpenMatchRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<List<MatchPreferredVenueResponse>>> SearchPreferredVenues(string? province, string? ward, double radiusKm = 5, double? latitude = null, double? longitude = null, CancellationToken cancellationToken = default);
    Task<ServiceResult<PaginatedResponse<MatchSearchResponse>>> GetOpenMatches(string? owner, string? matchType, int? skillLevel, DateOnly? from, DateOnly? to, string? province, string? ward, string? source, int page = 1, int pageSize = Pagination.DefaultPageSize, CancellationToken cancellationToken = default);
    Task<ServiceResult<PaginatedResponse<MatchSearchResponse>>> GetMyOpenMatches(int page = 1, int pageSize = Pagination.DefaultPageSize, CancellationToken cancellationToken = default);
    Task<ServiceResult<OpenMatchDetailResponse>> GetOpenMatchDetail(int matchId, bool reconcilePayments, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> UpdateOpenMatchInvitation(int matchId, UpdateOpenMatchInvitationRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> JoinOpenMatch(int matchId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> LeaveOpenMatch(int matchId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> AcceptParticipant(int matchId, int participantId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> RejectParticipant(int matchId, int participantId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> RemoveParticipant(int matchId, int participantId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> MarkReadyToBook(int matchId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> CreateMatchBooking(int matchId, CreateMatchBookingRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> CancelPendingMatchBooking(int matchId, CancellationToken cancellationToken);
    Task<ServiceResult<List<MatchSlotOptionResponse>>> GetMatchSlotOptions(int matchId, int venueId, DateOnly date, CancellationToken cancellationToken);
    Task<ServiceResult<List<MatchSlotOptionResponse>>> VoteMatchSlot(int matchId, MatchSlotVoteRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<List<MatchSlotOptionResponse>>> UnvoteMatchSlot(int matchId, MatchSlotVoteRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> ReportSlotUnavailable(int matchId, int bookingCheckInGroupId, ReportMatchSlotAbsenceRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> CancelSlotUnavailable(int matchId, int matchSlotAbsenceId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> ApplyForSlotReplacement(int matchId, int matchSlotAbsenceId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> WithdrawSlotReplacement(int matchId, int matchSlotAbsenceId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> AcceptSlotReplacement(int matchId, int matchSlotAbsenceId, int replacementRequestId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> RejectSlotReplacement(int matchId, int matchSlotAbsenceId, int replacementRequestId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> RemoveSlotReplacement(int matchId, int matchSlotAbsenceId, int replacementRequestId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> CompleteOpenMatch(int matchId, CancellationToken cancellationToken);
    Task<ServiceResult<MatchPlayerReviewResponse>> ReviewMatchPlayer(int matchId, int revieweePlayerId, CreateMatchPlayerReviewRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<List<MatchPlayerReviewResponse>>> GetMatchPlayerReviews(int matchId, CancellationToken cancellationToken);
    Task<ServiceResult<List<MatchPlayerReviewResponse>>> GetReceivedMatchPlayerReviews(CancellationToken cancellationToken);

    // Recommendations & Invitations
    Task<ServiceResult<List<MatchPlayerRecommendationResponse>>> GetPlayerRecommendations(double radiusKm = 5, double? latitude = null, double? longitude = null, string? province = null, string? ward = null, int minSkillLevel = 1, int maxSkillLevel = 5, int limit = 20, CancellationToken cancellationToken = default);
    Task<ServiceResult<OpenMatchDetailResponse>> InviteMatchPlayers(int matchId, InviteMatchPlayersRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> AcceptMatchInvitation(int matchId, CancellationToken cancellationToken);
    Task<ServiceResult<OpenMatchDetailResponse>> DeclineMatchInvitation(int matchId, CancellationToken cancellationToken);
}
