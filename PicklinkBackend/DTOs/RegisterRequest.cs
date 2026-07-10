using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class RegisterRequest
{
    [Required(ErrorMessage = "Vui lÃ²ng nháº­p tÃªn ngÆ°á»i dÃ¹ng.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "TÃªn ngÆ°á»i dÃ¹ng pháº£i tá»« 3 Ä‘áº¿n 100 kÃ½ tá»±.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lÃ²ng nháº­p email.")]
    [EmailAddress(ErrorMessage = "Email khÃ´ng há»£p lá»‡.")]
    [StringLength(255, ErrorMessage = "Email khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 255 kÃ½ tá»±.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lÃ²ng nháº­p máº­t kháº©u.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Máº­t kháº©u pháº£i tá»« 8 Ä‘áº¿n 100 kÃ½ tá»±.")]
    [RegularExpression(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).+$",
        ErrorMessage = "Máº­t kháº©u pháº£i cÃ³ chá»¯ hoa, chá»¯ thÆ°á»ng, sá»‘ vÃ  kÃ½ tá»± Ä‘áº·c biá»‡t.")]
    public string Password { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "TÃªn thÃ nh phá»‘ khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 100 kÃ½ tá»±.")]
    public string? City { get; set; }

    [StringLength(150, ErrorMessage = "TÃªn xÃ£/phÆ°á»ng khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 150 kÃ½ tá»±.")]
    public string? Commune { get; set; }

    [StringLength(500, ErrorMessage = "ÄÆ°á»ng dáº«n áº£nh Ä‘áº¡i diá»‡n khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 500 kÃ½ tá»±.")]
    public string? ProfileImageUrl { get; set; }
}
