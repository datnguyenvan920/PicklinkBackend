using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Match
{
    public int MatchId { get; set; }

    public string MatchType { get; set; } = null!;

    public int MatchSkillLevel { get; set; }

    public DateTime? MatchTime { get; set; }

    public string Status { get; set; } = null!;

    public TimeOnly? PreferredTimeStart { get; set; }

    public TimeOnly? PreferredTimeEnd { get; set; }

    public string? SharedVenues { get; set; }

    public int? Team1Id { get; set; }

    public int? Team2Id { get; set; }

    public int? WinningTeamId { get; set; }

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<MatchCheckIn> MatchCheckIns { get; set; } = new List<MatchCheckIn>();

    public virtual ICollection<MatchParticipant> MatchParticipants { get; set; } = new List<MatchParticipant>();

    public virtual ICollection<Scorecard> Scorecards { get; set; } = new List<Scorecard>();

    public virtual ICollection<SkillMatchup> SkillMatchups { get; set; } = new List<SkillMatchup>();

    public virtual Team? Team1 { get; set; }

    public virtual Team? Team2 { get; set; }

    public virtual Team? WinningTeam { get; set; }
}
