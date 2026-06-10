using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class TournamentTeam
{
    public int TournamentId { get; set; }
    public int TeamId { get; set; }

    public Tournament Tournament { get; set; } = null!;
    public Team Team { get; set; } = null!;
}
