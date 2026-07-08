using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class UserProfileResponse
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public string? City { get; set; }
    public string? Commune { get; set; }
    public int? PlayerId { get; set; }
    public double? SkillLevel { get; set; }
    public int? Prestige { get; set; }
    public string? PlayerSubType { get; set; }
    public string? PlayFrequency { get; set; }
    public string? PreferredTimeSlot { get; set; }
    public string? Bio { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Gender { get; set; }
    public double? HeightCm { get; set; }
    public double? WeightKg { get; set; }
    public int MatchesPlayed { get; set; }
    public List<MatchHistoryItemResponse> MatchHistory { get; set; } = new();
}

public class PublicPlayerProfileResponse
{
    public int PlayerId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public string? City { get; set; }
    public string? Commune { get; set; }
    public double SkillLevel { get; set; }
    public int Prestige { get; set; }
    public string? PlayerSubType { get; set; }
    public string? PlayFrequency { get; set; }
    public string? PreferredTimeSlot { get; set; }
    public string? Bio { get; set; }
    public int MatchesPlayed { get; set; }
}

public class MatchHistoryItemResponse
{
    public int MatchId { get; set; }
    public string MatchType { get; set; } = string.Empty;
    public int MatchSkillLevel { get; set; }
    public DateTime? MatchTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ParticipantClass { get; set; }
    public string? VenueName { get; set; }
    public int? CourtNumber { get; set; }
    public string? ScoreInfo { get; set; }
    public string? CheckInStatus { get; set; }
}

public class UpdateUserProfileRequest
{
    [Required(ErrorMessage = "Vui lÃ²ng nháº­p tÃªn ngÆ°á»i dÃ¹ng.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "TÃªn ngÆ°á»i dÃ¹ng pháº£i tá»« 3 Ä‘áº¿n 100 kÃ½ tá»±.")]
    public string Username { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "TÃªn thÃ nh phá»‘ khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 100 kÃ½ tá»±.")]
    public string? City { get; set; }

    [StringLength(150, ErrorMessage = "TÃªn xÃ£/phÆ°á»ng khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 150 kÃ½ tá»±.")]
    public string? Commune { get; set; }

    [StringLength(500, ErrorMessage = "ÄÆ°á»ng dáº«n áº£nh Ä‘áº¡i diá»‡n khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 500 kÃ½ tá»±.")]
    public string? ProfileImageUrl { get; set; }

    [Range(0, 5, ErrorMessage = "TrÃ¬nh Ä‘á»™ chÆ¡i pháº£i náº±m trong khoáº£ng 0 Ä‘áº¿n 5.")]
    public double SkillLevel { get; set; }

    [StringLength(50, ErrorMessage = "Phong cÃ¡ch chÆ¡i khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 50 kÃ½ tá»±.")]
    public string? PlayerSubType { get; set; }

    [StringLength(50, ErrorMessage = "Táº§n suáº¥t chÆ¡i khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 50 kÃ½ tá»±.")]
    public string? PlayFrequency { get; set; }

    [StringLength(50, ErrorMessage = "Khung giá» yÃªu thÃ­ch khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 50 kÃ½ tá»±.")]
    public string? PreferredTimeSlot { get; set; }

    [StringLength(500, ErrorMessage = "MÃ´ táº£ ngáº¯n khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 500 kÃ½ tá»±.")]
    public string? Bio { get; set; }

    public DateOnly? BirthDate { get; set; }

    [StringLength(30, ErrorMessage = "Giá»›i tÃ­nh khÃ´ng Ä‘Æ°á»£c vÆ°á»£t quÃ¡ 30 kÃ½ tá»±.")]
    public string? Gender { get; set; }

    [Range(50, 250, ErrorMessage = "Chiá»u cao pháº£i náº±m trong khoáº£ng 50 Ä‘áº¿n 250 cm.")]
    public double? HeightCm { get; set; }

    [Range(20, 250, ErrorMessage = "CÃ¢n náº·ng pháº£i náº±m trong khoáº£ng 20 Ä‘áº¿n 250 kg.")]
    public double? WeightKg { get; set; }
}
