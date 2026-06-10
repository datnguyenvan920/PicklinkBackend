using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Scorecard
{
    public int GameId { get; set; }

    public int MatchId { get; set; }

    public int CourtId { get; set; }

    public string? ScoreInfo { get; set; }

    public virtual Court Court { get; set; } = null!;

    public virtual Match Match { get; set; } = null!;
}
