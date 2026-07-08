using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class LoginRequest
{
    [Required(ErrorMessage = "Vui lÃ²ng nháº­p email.")]
    [EmailAddress(ErrorMessage = "Email khÃ´ng há»£p lá»‡.")]
    [StringLength(255, ErrorMessage = "Email khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 255 kÃ½ tá»±.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lÃ²ng nháº­p máº­t kháº©u.")]
    public string Password { get; set; } = string.Empty;
}
