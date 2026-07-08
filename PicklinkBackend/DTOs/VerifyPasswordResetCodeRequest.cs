using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class VerifyPasswordResetCodeRequest
{
    [Required(ErrorMessage = "Vui lÃ²ng nháº­p email.")]
    [EmailAddress(ErrorMessage = "Email khÃ´ng há»£p lá»‡.")]
    [StringLength(255, ErrorMessage = "Email khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 255 kÃ½ tá»±.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lÃ²ng nháº­p mÃ£ xÃ¡c thá»±c.")]
    [RegularExpression(@"^\d{8}$", ErrorMessage = "MÃ£ xÃ¡c thá»±c pháº£i gá»“m 8 chá»¯ sá»‘.")]
    public string Token { get; set; } = string.Empty;
}
