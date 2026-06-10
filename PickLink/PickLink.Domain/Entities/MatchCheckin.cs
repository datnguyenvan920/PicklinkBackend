using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class MatchCheckin
{
    public int CheckinId { get; set; }
    public int MatchId { get; set; }
    public int PlayerId { get; set; }
    public int? StaffId { get; set; }
    public string Status { get; set; } = "Present";
    public DateTime CheckedInAt { get; set; } = DateTime.UtcNow;

    public Match Match { get; set; } = null!;
    public Player Player { get; set; } = null!;
    public Staff? Staff { get; set; }
}