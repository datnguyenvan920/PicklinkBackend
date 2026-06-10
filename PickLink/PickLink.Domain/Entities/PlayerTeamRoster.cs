using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class PlayerTeamRoster
{
    public int PlayerId { get; set; }
    public int TeamId { get; set; }
    public DateOnly JoinedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public Player Player { get; set; } = null!;
    public Team Team { get; set; } = null!;
}
