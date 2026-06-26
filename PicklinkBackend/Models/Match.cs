using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Match
{
    public int MatchId { get; set; }

    public int? HostPlayerId { get; set; }

    public string MatchType { get; set; } = null!;

    public int MatchSkillLevel { get; set; }

    public int RequiredPlayerCount { get; set; }

    public DateTime? MatchTime { get; set; }

    public string Status { get; set; } = null!;

    public string? Title { get; set; }

    public string? Note { get; set; }

    public string? Province { get; set; }

    public string? Ward { get; set; }

    public double SearchRadiusKm { get; set; }

    public double? SearchLatitude { get; set; }

    public double? SearchLongitude { get; set; }

    public DateOnly? AvailableDateFrom { get; set; }

    public DateOnly? AvailableDateTo { get; set; }

    public int MinSkillLevel { get; set; }

    public int MaxSkillLevel { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public TimeOnly? PreferredTimeStart { get; set; }

    public TimeOnly? PreferredTimeEnd { get; set; }

    public string? SharedVenues { get; set; }

    public int? Team1Id { get; set; }

    public int? Team2Id { get; set; }

    public int? WinningTeamId { get; set; }

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<MatchCheckIn> MatchCheckIns { get; set; } = new List<MatchCheckIn>();

    public virtual ICollection<MatchParticipant> MatchParticipants { get; set; } = new List<MatchParticipant>();

    public virtual ICollection<MatchPlayerReview> MatchPlayerReviews { get; set; } = new List<MatchPlayerReview>();

    public virtual ICollection<Scorecard> Scorecards { get; set; } = new List<Scorecard>();

    public virtual ICollection<SkillMatchup> SkillMatchups { get; set; } = new List<SkillMatchup>();

    public virtual ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

    public virtual Team? Team1 { get; set; }

    public virtual Team? Team2 { get; set; }

    public virtual Team? WinningTeam { get; set; }

    public virtual Player? HostPlayer { get; set; }
}
