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
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(500)]
    public string? ProfileImageUrl { get; set; }
}
