using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class RatingHistory
{
    public int RatingId { get; set; }
    public int UserId { get; set; }
    public int TargetId { get; set; }
    public string TargetType { get; set; } = string.Empty; // Player | Venue
    public int Score { get; set; }  // 1–5, CHECK constraint in DbContext
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
