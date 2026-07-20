namespace PicklinkBackend.DTOs;

public class OwnerBookingResponse
{
    public int BookingId { get; set; }
    public int? MatchId { get; set; }
    public string? MatchType { get; set; }
    public int? RequiredPlayerCount { get; set; }
    public int? AcceptedPlayerCount { get; set; }
    public List<OwnerMatchPlayerResponse> MatchPlayers { get; set; } = new();
    public string BookingCode { get; set; } = string.Empty;
    public string BookingStatus { get; set; } = string.Empty;
    public string CheckInStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public int? PaymentId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal CourtAmount { get; set; }
    public decimal HourlyPrice { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string? VenuePhone { get; set; }
    public string Address { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string? PlayerEmail { get; set; }
    public string? PlayerCity { get; set; }
    public string? PlayerCommune { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public DateTime? CodeVerifiedAt { get; set; }
    public DateTime? PaymentConfirmedAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? NoShowAt { get; set; }
    public string? CodeVerifiedBy { get; set; }
    public string? PaymentConfirmedBy { get; set; }
    public string? CheckedInBy { get; set; }
    public string? NoShowBy { get; set; }
    public DateTime? PaymentPaidAt { get; set; }
    public DateTime? PaymentVerifiedAt { get; set; }
    public string? TransferCode { get; set; }
    public string? ReceiptImageUrl { get; set; }
    public string? RejectionReason { get; set; }
    public List<OwnerBookingHistoryResponse> BookingHistory { get; set; } = new();
    public List<OwnerPaymentHistoryResponse> PaymentHistory { get; set; } = new();
    public List<OwnerBookingSlotResponse> Slots { get; set; } = new();
    public List<OwnerBookingCheckInGroupResponse> CheckInGroups { get; set; } = new();
}

public class OwnerBookingSlotResponse
{
    public int BookingSlotId { get; set; }
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public decimal CourtAmount { get; set; }
}

public class OwnerBookingCheckInGroupResponse
{
    public int BookingCheckInGroupId { get; set; }
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string CheckInStatus { get; set; } = string.Empty;
}

public class OwnerMatchPlayerResponse
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class OwnerBookingHistoryResponse
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? ActorName { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class OwnerPaymentHistoryResponse
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? ActorName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OwnerRevenueReportResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal GrossRevenue { get; set; }
    public int PaidBookings { get; set; }
    public decimal PendingAmount { get; set; }
    public int CancelledBookings { get; set; }
    public decimal AverageBookingValue { get; set; }
    public List<OwnerDailyRevenueResponse> Daily { get; set; } = new();
    public List<OwnerBookingResponse> Bookings { get; set; } = new();
}

public class OwnerDailyRevenueResponse
{
    public DateOnly Date { get; set; }
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
}