using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class GoogleLoginRequest
{
    [Required(ErrorMessage = "PhiÃªn Ä‘Äƒng nháº­p Google khÃ´ng há»£p lá»‡. Vui lÃ²ng thá»­ láº¡i.")]
    public string IdToken { get; set; } = string.Empty;
}
