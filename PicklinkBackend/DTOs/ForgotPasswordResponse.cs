namespace PicklinkBackend.DTOs;

public class ForgotPasswordResponse
{
    public string Message { get; set; } = string.Empty;

    public DateTime? ExpiresAt { get; set; }
}
