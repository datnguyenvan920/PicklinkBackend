using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(255, ErrorMessage = "Email không được vượt quá 255 ký tự.")]
    public string Email { get; set; } = string.Empty;
}
