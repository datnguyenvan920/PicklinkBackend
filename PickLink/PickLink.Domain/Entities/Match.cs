using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

using PickLink.Domain.Enums;

public class Match
{
    public int MatchId { get; set; }
    public MatchType MatchType { get; set; }
    public float MatchSkillLevel { get; set; } = 0.0f;
    public DateTime MatchTime { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Scheduled;
    public int? Team1Id { get; set; }
    public int? Team2Id { get; set; }
    public int? WinningTeamId { get; set; }

    public Team? Team1 { get; set; }
    public Team? Team2 { get; set; }
    public Team? WinningTeam { get; set; }
    public ICollection<MatchParticipant> Participants { get; set; } = new List<MatchParticipant>();
    public ICollection<MatchCheckin> Checkins { get; set; } = new List<MatchCheckin>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<Scorecard> Scorecards { get; set; } = new List<Scorecard>();
    public ICollection<SkillMatchup> SkillMatchups { get; set; } = new List<SkillMatchup>();
}