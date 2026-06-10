using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class Player
{
    public int PlayerId { get; set; }
    public int UserId { get; set; }
    public int Prestige { get; set; } = 100;   // BR-14: default 100, not 0
    public float SkillLevel { get; set; } = 0.0f;
    public string? PlayerSubType { get; set; } // Amateur | Competitive | LeagueMember

    public User User { get; set; } = null!;
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<MatchParticipant> MatchParticipants { get; set; } = new List<MatchParticipant>();
    public ICollection<PlayerTeamRoster> TeamRosters { get; set; } = new List<PlayerTeamRoster>();
    public ICollection<MatchCheckin> Checkins { get; set; } = new List<MatchCheckin>();
    public ICollection<SkillMatchup> SkillMatchups { get; set; } = new List<SkillMatchup>();
    public ICollection<SocialGroup> OwnedGroups { get; set; } = new List<SocialGroup>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Team> CaptainedTeams { get; set; } = new List<Team>();
}
