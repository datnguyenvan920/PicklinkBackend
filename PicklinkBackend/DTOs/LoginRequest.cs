using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
