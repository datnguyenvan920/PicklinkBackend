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

public class MatchHistoryItemResponse
{
    public int MatchId { get; set; }
    public string MatchType { get; set; } = string.Empty;
    public int MatchSkillLevel { get; set; }
    public DateTime MatchTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ParticipantClass { get; set; }
    public string? VenueName { get; set; }
    public int? CourtNumber { get; set; }
    public string? ScoreInfo { get; set; }
    public string? CheckInStatus { get; set; }
}

public class UpdateUserProfileRequest
{
    [Required(ErrorMessage = "Vui lòng nhập tên người dùng.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Tên người dùng phải từ 3 đến 100 ký tự.")]
    public string Username { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Tên thành phố không được vượt quá 100 ký tự.")]
    public string? City { get; set; }

    [StringLength(150, ErrorMessage = "Tên xã/phường không được vượt quá 150 ký tự.")]
    public string? Commune { get; set; }

    [StringLength(500, ErrorMessage = "Đường dẫn ảnh đại diện không được vượt quá 500 ký tự.")]
    public string? ProfileImageUrl { get; set; }

    [Range(0, 5, ErrorMessage = "Trình độ chơi phải nằm trong khoảng 0 đến 5.")]
    public double SkillLevel { get; set; }

    [StringLength(50, ErrorMessage = "Phong cách chơi không được vượt quá 50 ký tự.")]
    public string? PlayerSubType { get; set; }

    [StringLength(50, ErrorMessage = "Tần suất chơi không được vượt quá 50 ký tự.")]
    public string? PlayFrequency { get; set; }

    [StringLength(50, ErrorMessage = "Khung giờ yêu thích không được vượt quá 50 ký tự.")]
    public string? PreferredTimeSlot { get; set; }

    [StringLength(500, ErrorMessage = "Mô tả ngắn không được vượt quá 500 ký tự.")]
    public string? Bio { get; set; }

    public DateOnly? BirthDate { get; set; }

    [StringLength(30, ErrorMessage = "Giới tính không được vượt quá 30 ký tự.")]
    public string? Gender { get; set; }

    [Range(50, 250, ErrorMessage = "Chiều cao phải nằm trong khoảng 50 đến 250 cm.")]
    public double? HeightCm { get; set; }

    [Range(20, 250, ErrorMessage = "Cân nặng phải nằm trong khoảng 20 đến 250 kg.")]
    public double? WeightKg { get; set; }
}
