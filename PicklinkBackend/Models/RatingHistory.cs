using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class RatingHistory
{
    public int RatingId { get; set; }

    public int UserId { get; set; }

    public int TargetId { get; set; }

    public string TargetType { get; set; } = null!;

    public int Score { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
