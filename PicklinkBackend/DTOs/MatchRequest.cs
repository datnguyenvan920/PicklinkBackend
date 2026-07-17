namespace PicklinkBackend.DTOs
{
    using System.ComponentModel.DataAnnotations;

    public class CreateMatchRequest
    {
        public string MatchType { get; set; } = null!;

        public int MatchSkillLevel { get; set; }

        public DateTime MatchTime { get; set; }

        public string Status { get; set; } = null!;
    }

    public class MatchVotingStatusResponse
    {
        public int MatchId { get; set; }
        public string Status { get; set; } = null!;
        public string PreferredTimeStart { get; set; } = null!;
        public string PreferredTimeEnd { get; set; } = null!;
        public List<CandidateSlotDto> CandidateSlots { get; set; } = new();
        public List<CandidateVenueDto> CandidateVenues { get; set; } = new();
        public List<ParticipantVoteDto> Votes { get; set; } = new();
    }

    public class CandidateSlotDto
    {
        public string Start { get; set; } = null!;
        public string End { get; set; } = null!;
    }

    public class CandidateVenueDto
    {
        public int VenueId { get; set; }
        public string VenueName { get; set; } = null!;
        public string Address { get; set; } = null!;
    }

    public class ParticipantVoteDto
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = null!;
        public string? PlayerProfilePictureUrl { get; set; }
        public int? VotedVenueId { get; set; }
        public string? VotedStartTime { get; set; }
        public string? VotedEndTime { get; set; }
    }

    public class CastVoteRequest
    {
        public int VenueId { get; set; }
        public string StartTime { get; set; } = null!;
    }

    public class MyMatchResponse
    {
        public int MatchId { get; set; }
        public string MatchType { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime? MatchTime { get; set; }
        public int MatchSkillLevel { get; set; }
        public string? PreferredTimeStart { get; set; }
        public string? PreferredTimeEnd { get; set; }
        public string? VenueName { get; set; }
        public int PlayerCount { get; set; }
    }

    public class MatchDetailResponse
    {
        public int MatchId { get; set; }
        public string MatchType { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime? MatchTime { get; set; }
        public string? VenueName { get; set; }
        public string? VenueAddress { get; set; }
        public double? VenueRating { get; set; }
        public string? VenueOpenTime { get; set; }
        public string? VenueCloseTime { get; set; }
        public string? VenuePhone { get; set; }
        public double? VenueLatitude { get; set; }
        public double? VenueLongitude { get; set; }
        public int? CourtNumber { get; set; }
        public int? ConversationId { get; set; }
        public TeamDetailDto? Team1 { get; set; }
        public TeamDetailDto? Team2 { get; set; }
    }

    public class TeamDetailDto
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; } = null!;
        public List<TeamPlayerDto> Players { get; set; } = new();
    }

    public class TeamPlayerDto
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = null!;
        public string? AvatarUrl { get; set; }
    }

    public class SendMatchMessageRequest
    {
        public string Content { get; set; } = null!;
    }

    public class CreateOpenMatchRequest
    {
        [Required]
        public string MatchType { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Province { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string Ward { get; set; } = string.Empty;

        [Range(0.5, 10)]
        public double SearchRadiusKm { get; set; }

        [Range(-90, 90)]
        public double? SearchLatitude { get; set; }

        [Range(-180, 180)]
        public double? SearchLongitude { get; set; }

        [MinLength(1)]
        public List<int> PreferredVenueIds { get; set; } = [];

        public DateOnly AvailableDateFrom { get; set; }

        public DateOnly AvailableDateTo { get; set; }

        [Required]
        public string PreferredTimeStart { get; set; } = string.Empty;

        [Required]
        public string PreferredTimeEnd { get; set; } = string.Empty;

        [MaxLength(20)]
        public List<MatchAvailabilitySlotRequest> AvailabilitySlots { get; set; } = [];

        [Range(1, 5)]
        public int MinSkillLevel { get; set; }

        [Range(1, 5)]
        public int MaxSkillLevel { get; set; }

        [Range(1, 8)]
        public int NeededPlayerCount { get; set; }

        [MaxLength(1000)]
        public string? Note { get; set; }
    }

    public class UpdateOpenMatchInvitationRequest
    {
        [Required]
        public string MatchType { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Province { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string Ward { get; set; } = string.Empty;

        [Range(0.5, 10)]
        public double SearchRadiusKm { get; set; }

        [Range(-90, 90)]
        public double? SearchLatitude { get; set; }

        [Range(-180, 180)]
        public double? SearchLongitude { get; set; }

        [MinLength(1)]
        public List<int> PreferredVenueIds { get; set; } = [];

        public DateOnly AvailableDateFrom { get; set; }

        public DateOnly AvailableDateTo { get; set; }

        [Required]
        public string PreferredTimeStart { get; set; } = string.Empty;

        [Required]
        public string PreferredTimeEnd { get; set; } = string.Empty;

        [MaxLength(20)]
        public List<MatchAvailabilitySlotRequest> AvailabilitySlots { get; set; } = [];

        [Range(1, 5)]
        public int MinSkillLevel { get; set; }

        [Range(1, 5)]
        public int MaxSkillLevel { get; set; }

        [Range(1, 8)]
        public int NeededPlayerCount { get; set; }

        [MaxLength(1000)]
        public string? Note { get; set; }
    }

    public class MatchAvailabilitySlotRequest
    {
        [Required]
        public string TimeStart { get; set; } = string.Empty;

        [Required]
        public string TimeEnd { get; set; } = string.Empty;
    }

    public class MatchAvailabilitySlotResponse
    {
        public int MatchAvailabilitySlotId { get; set; }
        public string TimeStart { get; set; } = string.Empty;
        public string TimeEnd { get; set; } = string.Empty;
    }

    public class CreateMatchBookingSlotRequest
    {
        [Range(1, int.MaxValue)]
        public int CourtId { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }
    }

    public class CreateMatchBookingRequest
    {
        [Required, MinLength(1), MaxLength(496)]
        public List<CreateMatchBookingSlotRequest> Slots { get; set; } = [];
    }

    public class MatchPreferredVenueResponse
    {
        public int VenueId { get; set; }
        public string VenueName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? DistanceKm { get; set; }
    }

    public class MatchSearchResponse
    {
        public int MatchId { get; set; }
        public int HostPlayerId { get; set; }
        public string HostName { get; set; } = string.Empty;
        public string? HostAvatarUrl { get; set; }
        public string MatchType { get; set; } = string.Empty;
        public int MatchSkillLevel { get; set; }
        public int MinSkillLevel { get; set; }
        public int MaxSkillLevel { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Note { get; set; }
        public string Province { get; set; } = string.Empty;
        public string Ward { get; set; } = string.Empty;
        public double SearchRadiusKm { get; set; }
        public double? SearchLatitude { get; set; }
        public double? SearchLongitude { get; set; }
        public DateOnly AvailableDateFrom { get; set; }
        public DateOnly AvailableDateTo { get; set; }
        public string PreferredTimeStart { get; set; } = string.Empty;
        public string PreferredTimeEnd { get; set; } = string.Empty;
        public List<MatchAvailabilitySlotResponse> AvailabilitySlots { get; set; } = [];
        public int NeededPlayerCount { get; set; }
        public int RequiredPlayerCount { get; set; }
        public int AcceptedPlayerCount { get; set; }
        public int PendingRequestCount { get; set; }
        public int AvailableSlotCount { get; set; }
        public List<MatchPreferredVenueResponse> PreferredVenues { get; set; } = [];
        public int? CourtId { get; set; }
        public int? CourtNumber { get; set; }
        public int? VenueId { get; set; }
        public string? VenueName { get; set; }
        public string? Address { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public decimal TotalBookingAmount { get; set; }
        public decimal AmountPerPlayer { get; set; }
        public bool IsHost { get; set; }
        public string? MyParticipantStatus { get; set; }
        public string? MyPaymentStatus { get; set; }
    }

    public class MatchParticipantResponse
    {
        public int ParticipantId { get; set; }
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public double SkillLevel { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsHost { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
        public int? PaymentId { get; set; }
        public string? PaymentStatus { get; set; }
        public string? QrImageUrl { get; set; }
        public string? TransferContent { get; set; }
        public string? PaymentRejectionReason { get; set; }
        public string CheckInStatus { get; set; } = "Pending";
        public DateTime? CheckedInAt { get; set; }
    }

    public class MatchPlayerRecommendationResponse
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public double SkillLevel { get; set; }
        public int Prestige { get; set; }
        public string? City { get; set; }
        public string? Commune { get; set; }
        public string? PreferredTimeSlot { get; set; }
        public double? DistanceKm { get; set; }
        public string MatchReason { get; set; } = string.Empty;
    }

    public class InviteMatchPlayersRequest
    {
        public bool Automatic { get; set; }

        [MaxLength(20)]
        public List<int> PlayerIds { get; set; } = [];
    }

    public class OpenMatchDetailResponse : MatchSearchResponse
    {
        public int? BookingId { get; set; }
        public int? ConversationId { get; set; }
        public int? MyPlayerId { get; set; }
        public string? CheckInCode { get; set; }
        public List<MatchBookingCheckInResponse> BookingCheckIns { get; set; } = [];
        public DateTime? PaymentDeadline { get; set; }
        public int? MyPaymentId { get; set; }
        public string? MyQrImageUrl { get; set; }
        public string? MyTransferContent { get; set; }
        public string? MyPaymentRejectionReason { get; set; }
        public List<MatchParticipantResponse> Participants { get; set; } = [];
    }

    public class MatchBookingCheckInResponse
    {
        public int BookingId { get; set; }
        public string BookingStatus { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<MatchBookingCheckInGroupResponse> CheckInGroups { get; set; } = [];
    }

    public class MatchBookingCheckInGroupResponse
    {
        public int BookingCheckInGroupId { get; set; }
        public int CourtId { get; set; }
        public int CourtNumber { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string? CheckInCode { get; set; }
        public string CheckInStatus { get; set; } = string.Empty;
        public bool IsCheckInWindowOpen { get; set; }
    }

    public class CreateMatchPlayerReviewRequest
    {
        [Range(1, 5)]
        public int Score { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }
    }

    public class MatchPlayerReviewResponse
    {
        public int MatchPlayerReviewId { get; set; }
        public int MatchId { get; set; }
        public int ReviewerPlayerId { get; set; }
        public string ReviewerName { get; set; } = string.Empty;
        public int RevieweePlayerId { get; set; }
        public string RevieweeName { get; set; } = string.Empty;
        public int Score { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
