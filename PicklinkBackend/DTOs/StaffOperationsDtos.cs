namespace PicklinkBackend.DTOs;

public record VerifyBookingCodeRequest(string Code);

public record StaffNotificationResponse(string Type, string Title, string Message, int BookingId, DateTime StartTime);

public class StaffAssignmentResponse
{
    public int StaffId { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = Array.Empty<string>();
}

public class StaffBookingResponse
{
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string BookingType { get; set; } = "Court";
    public int? MatchId { get; set; }
    public int? VerifiedPlayerId { get; set; }
    public int? VerifiedCheckInGroupId { get; set; }
    public string BookingStatus { get; set; } = string.Empty;
    public string CheckInStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int ParticipantCount { get; set; } = 1;
    public int CheckedInParticipantCount { get; set; }
    public List<StaffMatchParticipantResponse> Participants { get; set; } = [];
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsCheckInWindowOpen { get; set; }
    public bool CanMarkNoShow { get; set; }
    public DateTime? CodeVerifiedAt { get; set; }
    public DateTime? PaymentConfirmedAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? NoShowAt { get; set; }
    public List<StaffCheckInGroupResponse> CheckInGroups { get; set; } = [];
}

public class StaffCheckInGroupResponse
{
    public int BookingCheckInGroupId { get; set; }
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string CheckInStatus { get; set; } = string.Empty;
    public bool IsCheckInWindowOpen { get; set; }
    public bool CanMarkNoShow { get; set; }
    public DateTime? CodeVerifiedAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? NoShowAt { get; set; }
}

public class StaffMatchParticipantResponse
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string AttendanceStatus { get; set; } = "Pending";
    public DateTime? AttendanceAt { get; set; }
}
