using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Staff
{
    public int StaffId { get; set; }

    public int UserId { get; set; }

    public int VenueId { get; set; }

    public string Role { get; set; } = null!;

    public string Permissions { get; set; } = "ViewBookings,VerifyBooking,ConfirmPayment,CheckIn,MarkNoShow";

    public bool IsActive { get; set; } = true;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public int? AssignedByUserId { get; set; }

    public DateTime? RevokedAt { get; set; }

    public virtual ICollection<MatchCheckIn> MatchCheckIns { get; set; } = new List<MatchCheckIn>();

    public virtual ICollection<SessionTicket> CheckedInSessionTickets { get; set; } = new List<SessionTicket>();

    public virtual User User { get; set; } = null!;

    public virtual Venue Venue { get; set; } = null!;
}
