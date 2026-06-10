using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Tournament
{
    public int TournamentId { get; set; }

    public string Name { get; set; } = null!;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string Status { get; set; } = null!;

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
}
