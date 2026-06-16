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
}
