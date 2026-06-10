using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class SkillMatchup
{
    public int MatchupId { get; set; }

    public int PlayerId { get; set; }

    public int MatchId { get; set; }

    public int SkillDelta { get; set; }

    public virtual Match Match { get; set; } = null!;

    public virtual Player Player { get; set; } = null!;
}
