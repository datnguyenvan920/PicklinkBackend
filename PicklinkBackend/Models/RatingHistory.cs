using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class RatingHistory
{
    public int RatingId { get; set; }

    public int UserId { get; set; }

    public int? BookingId { get; set; }

    public int TargetId { get; set; }

    public string TargetType { get; set; } = null!;

    public int Score { get; set; }

    public string? Comment { get; set; }

    public string? Tags { get; set; }

    public bool IsAnonymous { get; set; }

    public bool IsHidden { get; set; }

    public string ModerationStatus { get; set; } = "Visible";

    public string? ModerationNote { get; set; }

    public DateTime? ModeratedAt { get; set; }

    public int? ModeratedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual User? ModeratedByUser { get; set; }

    public virtual Booking? Booking { get; set; }
}
