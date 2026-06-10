using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Team
{
    public int TeamId { get; set; }

    public string TeamName { get; set; } = null!;

    public int CaptainId { get; set; }

    public string? Description { get; set; }

    public virtual Player Captain { get; set; } = null!;

    public virtual ICollection<Match> MatchTeam1s { get; set; } = new List<Match>();

    public virtual ICollection<Match> MatchTeam2s { get; set; } = new List<Match>();

    public virtual ICollection<Match> MatchWinningTeams { get; set; } = new List<Match>();

    public virtual ICollection<PlayerTeamRoster> PlayerTeamRosters { get; set; } = new List<PlayerTeamRoster>();

    public virtual ICollection<Tournament> Tournaments { get; set; } = new List<Tournament>();
}
