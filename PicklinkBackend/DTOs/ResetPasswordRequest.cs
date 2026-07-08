using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "Vui lÃ²ng nháº­p email.")]
    [EmailAddress(ErrorMessage = "Email khÃ´ng há»£p lá»‡.")]
    [StringLength(255, ErrorMessage = "Email khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 255 kÃ½ tá»±.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lÃ²ng nháº­p mÃ£ Ä‘áº·t láº¡i máº­t kháº©u.")]
    [StringLength(64, ErrorMessage = "MÃ£ Ä‘áº·t láº¡i máº­t kháº©u khÃ´ng há»£p lá»‡.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lÃ²ng nháº­p máº­t kháº©u má»›i.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Máº­t kháº©u pháº£i tá»« 8 Ä‘áº¿n 100 kÃ½ tá»±.")]
    [RegularExpression(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).+$",
        ErrorMessage = "Máº­t kháº©u pháº£i cÃ³ chá»¯ hoa, chá»¯ thÆ°á»ng, sá»‘ vÃ  kÃ½ tá»± Ä‘áº·c biá»‡t.")]
    public string NewPassword { get; set; } = string.Empty;
}
