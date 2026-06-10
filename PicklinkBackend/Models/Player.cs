using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Player
{
    public int PlayerId { get; set; }

    public int UserId { get; set; }

    public int Prestige { get; set; }

    public double SkillLevel { get; set; }

    public string? PlayerSubType { get; set; }

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<MatchCheckIn> MatchCheckIns { get; set; } = new List<MatchCheckIn>();

    public virtual ICollection<MatchParticipant> MatchParticipants { get; set; } = new List<MatchParticipant>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<PlayerTeamRoster> PlayerTeamRosters { get; set; } = new List<PlayerTeamRoster>();

    public virtual ICollection<SkillMatchup> SkillMatchups { get; set; } = new List<SkillMatchup>();

    public virtual ICollection<SocialGroup> SocialGroups { get; set; } = new List<SocialGroup>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();

    public virtual User User { get; set; } = null!;
}
