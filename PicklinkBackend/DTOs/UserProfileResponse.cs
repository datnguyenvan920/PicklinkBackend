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
    public int? PlayerId { get; set; }
    public double? SkillLevel { get; set; }
    public int? Prestige { get; set; }
    public string? PlayerSubType { get; set; }
    public string? DominantHand { get; set; }
    public string? PreferredPosition { get; set; }
    public string? Bio { get; set; }
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

    [StringLength(500, ErrorMessage = "Đường dẫn ảnh đại diện không được vượt quá 500 ký tự.")]
    public string? ProfileImageUrl { get; set; }

    [Range(0, 5, ErrorMessage = "Trình độ chơi phải nằm trong khoảng 0 đến 5.")]
    public double SkillLevel { get; set; }

    [StringLength(50, ErrorMessage = "Phong cách chơi không được vượt quá 50 ký tự.")]
    public string? PlayerSubType { get; set; }

    [StringLength(50, ErrorMessage = "Tay thuận không được vượt quá 50 ký tự.")]
    public string? DominantHand { get; set; }

    [StringLength(100, ErrorMessage = "Vị trí hay chơi không được vượt quá 100 ký tự.")]
    public string? PreferredPosition { get; set; }

    [StringLength(500, ErrorMessage = "Mô tả ngắn không được vượt quá 500 ký tự.")]
    public string? Bio { get; set; }
}
