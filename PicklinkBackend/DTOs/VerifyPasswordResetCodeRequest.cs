using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class VerifyPasswordResetCodeRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(255, ErrorMessage = "Email không được vượt quá 255 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mã xác thực.")]
    [RegularExpression(@"^\d{8}$", ErrorMessage = "Mã xác thực phải gồm 8 chữ số.")]
    public string Token { get; set; } = string.Empty;
}
