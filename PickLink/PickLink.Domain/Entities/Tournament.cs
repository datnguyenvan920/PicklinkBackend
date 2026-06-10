using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class Tournament
{
    public int TournamentId { get; set; }
    public int OrganizerId { get; set; }  // SC-06: community owner
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = "Upcoming";

    public Player Organizer { get; set; } = null!;
    public ICollection<TournamentTeam> TournamentTeams { get; set; } = new List<TournamentTeam>();
}
