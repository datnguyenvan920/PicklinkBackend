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

    public class InitMatchFromLobbyRequest
    {
        public string LobbyType { get; set; } = null!;
        public string PreferredTimeStart { get; set; } = null!;
        public string PreferredTimeEnd { get; set; } = null!;
        public List<int> SharedVenues { get; set; } = new();
        public List<int> PlayerIds { get; set; } = new();
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
        [Range(1, int.MaxValue)]
        public int CourtId { get; set; }

        [Required]
        public string MatchType { get; set; } = string.Empty;

        [Range(1, 5)]
        public int MatchSkillLevel { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        [MaxLength(1000)]
        public string? Note { get; set; }
    }

    public class MatchSearchResponse
    {
        public int MatchId { get; set; }
        public int HostPlayerId { get; set; }
        public string HostName { get; set; } = string.Empty;
        public string? HostAvatarUrl { get; set; }
        public string MatchType { get; set; } = string.Empty;
        public int MatchSkillLevel { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int RequiredPlayerCount { get; set; }
        public int AcceptedPlayerCount { get; set; }
        public int PendingRequestCount { get; set; }
        public int AvailableSlotCount { get; set; }
        public int CourtId { get; set; }
        public int CourtNumber { get; set; }
        public int VenueId { get; set; }
        public string VenueName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double TotalBookingAmount { get; set; }
        public double AmountPerPlayer { get; set; }
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
        public string? PaymentStatus { get; set; }
    }

    public class OpenMatchDetailResponse : MatchSearchResponse
    {
        public int BookingId { get; set; }
        public int? MyPlayerId { get; set; }
        public string? CheckInCode { get; set; }
        public DateTime? PaymentDeadline { get; set; }
        public int? MyPaymentId { get; set; }
        public string? MyQrImageUrl { get; set; }
        public string? MyTransferContent { get; set; }
        public List<MatchParticipantResponse> Participants { get; set; } = [];
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
