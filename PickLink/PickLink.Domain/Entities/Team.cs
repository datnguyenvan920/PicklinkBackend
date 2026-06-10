using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PickLink.Domain.Entities;

public class Team
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int CaptainId { get; set; }
    public string? Description { get; set; }

    public Player Captain { get; set; } = null!;
    public ICollection<PlayerTeamRoster> PlayerRosters { get; set; } = new List<PlayerTeamRoster>();
    public ICollection<Match> Team1Matches { get; set; } = new List<Match>();
    public ICollection<Match> Team2Matches { get; set; } = new List<Match>();
    public ICollection<TournamentTeam> TournamentTeams { get; set; } = new List<TournamentTeam>();
}
