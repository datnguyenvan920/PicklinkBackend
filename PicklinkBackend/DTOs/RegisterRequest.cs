using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class RegisterRequest
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 100 characters.")]
    [RegularExpression(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).+$",
        ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character.")]
    public string Password { get; set; } = string.Empty;

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(500)]
    public string? ProfileImageUrl { get; set; }
}
