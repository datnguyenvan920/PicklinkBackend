using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class MatchParticipant
{
    public int ParticipantId { get; set; }

    public int MatchId { get; set; }

    public int PlayerId { get; set; }

    public string? Class { get; set; }

    public int? VotedVenueId { get; set; }

    public TimeOnly? VotedStartTime { get; set; }

    public TimeOnly? VotedEndTime { get; set; }

    public virtual Match Match { get; set; } = null!;

    public virtual Player Player { get; set; } = null!;
}
