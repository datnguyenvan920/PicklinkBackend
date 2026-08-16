namespace PicklinkBackend.DTOs;

public class PlayerScheduleEntryResponse
{
    /// <summary>Booking | Ticket | Match — decides the badge and the detail route the client links to.</summary>
    public string EntryType { get; set; } = string.Empty;

    /// <summary>Id of the thing the player opens: bookingId, sessionTicketId or matchId.</summary>
    public int ReferenceId { get; set; }

    public int BookingId { get; set; }

    /// <summary>Vietnam-local play date, so the client groups by day without re-deriving a time zone.</summary>
    public DateOnly Date { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>True while the player still owes something: an unpaid hold, or a ticket on hold.</summary>
    public bool NeedsAction { get; set; }

    public decimal Amount { get; set; }
    public string? Code { get; set; }
    public string? MatchType { get; set; }
}

public class PlayerScheduleResponse
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public List<PlayerScheduleEntryResponse> Entries { get; set; } = [];
}
