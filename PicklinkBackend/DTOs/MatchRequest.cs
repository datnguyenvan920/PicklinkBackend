namespace PicklinkBackend.DTOs
{
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
}
