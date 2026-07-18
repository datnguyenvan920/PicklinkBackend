using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public sealed class CreateTicketSessionRequest
{
    [Range(1, int.MaxValue)]
    public int VenueId { get; set; }

    [Range(1, int.MaxValue)]
    public int CourtId { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required, StringLength(50, MinimumLength = 1)]
    public string SkillLevel { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 1)]
    public string PlayFormat { get; set; } = string.Empty;

    [Range(1, 100)]
    public int MaxPlayers { get; set; }

    [Range(typeof(decimal), "0", "100000000")]
    public decimal TicketPrice { get; set; }
}

public sealed class UpdateTicketSessionRequest
{
    [Range(1, int.MaxValue)]
    public int? VenueId { get; set; }

    [Range(1, int.MaxValue)]
    public int? CourtId { get; set; }

    public DateOnly? Date { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required, StringLength(50, MinimumLength = 1)]
    public string SkillLevel { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 1)]
    public string PlayFormat { get; set; } = string.Empty;

    [Range(1, 100)]
    public int MaxPlayers { get; set; }

    [Range(typeof(decimal), "0", "100000000")]
    public decimal TicketPrice { get; set; }
}

public sealed class CancelTicketSessionRequest
{
    [Required, StringLength(400, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class CancelSessionTicketRequest
{
    [StringLength(500)]
    public string? Reason { get; set; }
}

public sealed class CompleteTicketRefundRequest
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Reference { get; set; } = string.Empty;
}

public sealed class CheckInSessionTicketRequest
{
    [Required, StringLength(40, MinimumLength = 3)]
    public string TicketCode { get; set; } = string.Empty;
}

public sealed class TicketSessionResponse
{
    public int TicketSessionId { get; set; }
    public int BookingId { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string VenueAddress { get; set; } = string.Empty;
    public string? VenuePhone { get; set; }
    public double? VenueLatitude { get; set; }
    public double? VenueLongitude { get; set; }
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public string? CourtType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SkillLevel { get; set; } = string.Empty;
    public string PlayFormat { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int MaxPlayers { get; set; }
    public int SoldTickets { get; set; }
    public int ReservedTickets { get; set; }
    public int RemainingTickets { get; set; }
    public decimal TicketPrice { get; set; }
    public int CancellationDeadlineHours { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}

public sealed class SessionTicketResponse
{
    public int SessionTicketId { get; set; }
    public int TicketSessionId { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string? PlayerEmail { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public int? CheckedInByStaffId { get; set; }
    public int PaymentId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? TransferContent { get; set; }
    public string? BankCode { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountName { get; set; }
    public string? QrImageUrl { get; set; }
    public DateTime? PaidAt { get; set; }
    public List<SePayTransactionResponse> SePayTransactions { get; set; } = [];
    public TicketSessionResponse? Session { get; set; }
}

public sealed class SePayTransactionResponse
{
    public int SePayTransactionId { get; set; }
    public long ExternalTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public string? RefundReference { get; set; }
}

public sealed class TicketSessionParticipantsResponse
{
    public TicketSessionResponse Session { get; set; } = new();
    public List<SessionTicketResponse> Tickets { get; set; } = [];
}

public sealed class StaffTicketSessionParticipantsResponse
{
    public TicketSessionResponse Session { get; set; } = new();
    public List<StaffTicketParticipantResponse> Tickets { get; set; } = [];
}

public sealed class StaffTicketParticipantResponse
{
    public int SessionTicketId { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public string TicketStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public int? CheckedInByStaffId { get; set; }
}
