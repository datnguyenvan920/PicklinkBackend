namespace PicklinkBackend.DTOs;

public record AssignStaffRequest(int VenueId, string Email, string? Role, List<string>? Permissions);
public record UpdateStaffRequest(string? Role, List<string>? Permissions, bool IsActive);

public class CreateStaffAccountRequest
{
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
    public int VenueId { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    [System.ComponentModel.DataAnnotations.StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 8)]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).+$", ErrorMessage = "Mat khau phai co chu hoa, chu thuong, so va ky tu dac biet.")]
    public string Password { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.StringLength(100)]
    public string? Role { get; set; }

    public List<string>? Permissions { get; set; }
}

public class OwnerStaffResponse
{
    public int StaffId { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = Array.Empty<string>();
    public bool IsActive { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class OwnerCheckInHistoryResponse
{
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public int CourtNumber { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public string CheckInStatus { get; set; } = string.Empty;
    public DateTime? CodeVerifiedAt { get; set; }
    public string? CodeVerifiedBy { get; set; }
    public DateTime? PaymentConfirmedAt { get; set; }
    public string? PaymentConfirmedBy { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public string? CheckedInBy { get; set; }
    public DateTime? NoShowAt { get; set; }
    public string? NoShowBy { get; set; }
}