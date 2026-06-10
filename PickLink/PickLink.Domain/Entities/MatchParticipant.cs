using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class MatchParticipant
{
    public int ParticipantId { get; set; }
    public int MatchId { get; set; }
    public int PlayerId { get; set; }
    public string? Class { get; set; }

    public Match Match { get; set; } = null!;
    public Player Player { get; set; } = null!;
}
