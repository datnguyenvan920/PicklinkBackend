using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public sealed class AdminUserLockRequest
{
    public string? Reason { get; set; }
}

public sealed class AdminCreateVenueOwnerRequest
{
    [Required(ErrorMessage = "Vui lòng nhập tên người dùng.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Tên người dùng phải từ 3 đến 100 ký tự.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(255, ErrorMessage = "Email không được vượt quá 255 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ 8 đến 100 ký tự.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).+$", ErrorMessage = "Mật khẩu phải có chữ hoa, chữ thường, số và ký tự đặc biệt.")]
    public string Password { get; set; } = string.Empty;
    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [StringLength(30, ErrorMessage = "Số điện thoại không được vượt quá 30 ký tự.")]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class AdminUserResponse
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public string? LockReason { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedByName { get; set; }
    public DateTime? UnlockedAt { get; set; }
    public string? UnlockedByName { get; set; }
    public string? City { get; set; }
    public string? Commune { get; set; }
    public string? AvatarUrl { get; set; }
    public int JoinedClubCount { get; set; }
    public int OwnedVenueCount { get; set; }
    public int BookingCount { get; set; }
}

public class AdminUserSummaryResponse : AdminUserResponse {}
