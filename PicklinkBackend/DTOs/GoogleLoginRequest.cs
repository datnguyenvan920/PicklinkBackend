using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class GoogleLoginRequest
{
    [Required(ErrorMessage = "Phiên đăng nhập Google không hợp lệ. Vui lòng thử lại.")]
    public string IdToken { get; set; } = string.Empty;
}
