using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class GoogleLoginRequest
{
    [Required]
    public string IdToken { get; set; } = string.Empty;
}
