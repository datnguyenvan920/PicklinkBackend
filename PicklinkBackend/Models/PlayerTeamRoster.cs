using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class PlayerTeamRoster
{
    public int PlayerId { get; set; }

    public int TeamId { get; set; }

    public DateOnly JoinedDate { get; set; }

    public virtual Player Player { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
