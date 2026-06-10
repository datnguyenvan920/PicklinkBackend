using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class SkillMatchup
{
    public int MatchupId { get; set; }
    public int PlayerId { get; set; }
    public int MatchId { get; set; }
    public int SkillDelta { get; set; } = 0;
    public float SkillBefore { get; set; } = 0.0f; // BR-15 audit
    public float SkillAfter { get; set; } = 0.0f;  // BR-15 audit

    public Player Player { get; set; } = null!;
    public Match Match { get; set; } = null!;
}
