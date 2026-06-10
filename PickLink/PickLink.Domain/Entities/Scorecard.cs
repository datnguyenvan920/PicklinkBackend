using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class Scorecard
{
    public int GameId { get; set; }
    public int MatchId { get; set; }
    public int CourtId { get; set; }
    public int Team1Score { get; set; } = 0;
    public int Team2Score { get; set; } = 0;
    public int SetNumber { get; set; } = 1;
    public int? SubmittedByPlayerId { get; set; } // BR-05: captain submits

    public Match Match { get; set; } = null!;
    public Court Court { get; set; } = null!;
}
